using Tsvrc.Player;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace Tsvrc.Core
{
    /// <summary>
    /// Base class for networked, owner-driven processes: start/stop/complete lifecycle,
    /// ownership handoff, and periodic ticks, scoped to the process owner.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    [TsWorldExtensionPoint("TsProcess")]
    public class Process : TsvrcBehaviour
    {
        protected override bool IsTsvrcInternal => true;

        [UdonSynced] private bool _isRunning = false;

        private int _localPlayerId = -1;

        // Blocks TakeOverAbandonedProcess from re-firing once ownership is settled
        private bool _ownershipEstablished = false;

        [UdonSynced] private bool _useProcessUpdate = false;

        private const float _processUpdateInterval = 0.5f;
        // How often a running, owned process re-broadcasts its full synced state. See the class
        // remarks above for why this exists. Short enough that a missed discrete event is only
        // ever visibly wrong for a few seconds; long enough that idle processes cost negligible
        // bandwidth.
        private const float _autoResyncInterval = 5f;

        // Tracks whether the tick loop is currently scheduled to prevent scheduling it twice.
        // Runs for every running, owned process regardless of _useProcessUpdate - it drives the
        // resync heartbeat unconditionally and OnProcessUpdate only when a subclass opted in.
        private bool _updateLoopActive = false;
        // Real-time (Time.realtimeSinceStartup) deadline the next legitimate tick is due at.
        // See _TickProcessUpdate for how this is used to discard stale scheduled calls.
        private float _nextTickDueAtRealTime = 0f;
        // Real-time deadline the next resync broadcast is due at. Advanced independently of
        // _nextTickDueAtRealTime since the two run on different cadences.
        private float _nextResyncDueAtRealTime = 0f;

        // Set to true just before and cleared just after every SendCustomNetworkEvent(All, ...) call.
        // On the sending client VRChat fires the event inline before returning, so the broadcast
        // handler runs inside our own call stack. This flag lets the handler's CallingPlayer guard
        // know the inline execution is legitimate, even when CallingPlayer carries a value from an
        // outer event context. This is safe because Udon is single-threaded.
        protected bool _isBroadcasting = false;

        protected override void TsStart()
        {
            base.TsStart();

            _localPlayerId = Networking.LocalPlayer.playerId;
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            base.OnPlayerLeft(player);

            if (!IsProcessRunning() || !IsProcessOwner()) return;

            TakeOverAbandonedProcess();
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            base.OnOwnershipTransferred(player);

            // Backstop for OnPlayerLeft, in case it ran before ownership actually landed.
            if (!IsProcessRunning() || !IsProcessOwner()) return;

            TakeOverAbandonedProcess();
        }

        public override void OnPlayerSuspendChanged(VRCPlayerApi player)
        {
            base.OnPlayerSuspendChanged(player);

            // A suspended device stops running Udon and stops responding to network events:
            // https://creators.vrchat.com/worlds/udon/players/#get-issuspended
            // If the owner suspends, the tick loop stalls with nobody left able to restart it.
            if (!player.isSuspended || !IsProcessRunning() || !Networking.IsOwner(player, gameObject)) return;

            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        public override void OnDeserialization()
        {
            base.OnDeserialization();

            // Restart the tick loop if the local player is the owner and it is not currently running.
            // Covers stale-packet recovery and any other situation where the loop went silent.
            if (IsProcessOwner() && IsProcessRunning())
            {
                StartTickLoopIfNeeded();
            }
        }

        /// <summary>
        /// Starts the process. Safe to call from anyone: if the local player is not the owner, this just
        /// asks the owner to start it.
        /// </summary>
        /// <remarks>
        /// Does not change who owns the object. If you want the local player to become the
        /// owner, call <see cref="SetProcessOwner"/> yourself before calling this.
        /// </remarks>
        /// <param name="useProcessUpdate">Pass <c>true</c> to get an update tick every 0.5s while running.</param>
        public virtual void StartProcess(bool useProcessUpdate = false)
        {
            if (IsProcessRunning())
            {
                LogWarning("Process is already running, ignoring start call.");
                return;
            }

            if (!IsProcessOwner())
            {
                SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RequestStartProcess), useProcessUpdate);
                return;
            }

            ExecuteStart(useProcessUpdate);
        }

        [NetworkCallable(maxEventsPerSecond: 1)]
        public void RequestStartProcess(bool useProcessUpdate = false)
        {
            if (!IsProcessOwner() || IsProcessRunning())
            {
                LogWarning("RequestStartProcess rejected: not the owner or already running.");
                return;
            }

            ExecuteStart(useProcessUpdate);
        }

        private void ExecuteStart(bool useProcessUpdate)
        {
            _isRunning = true;
            _useProcessUpdate = useProcessUpdate;
            _ownershipEstablished = true;

            OnProcessStarted();
            RequestSerialization();

            StartTickLoopIfNeeded();
        }

        /// <summary>
        /// Forcibly stops the Process before completion.
        /// If called by a non-owner, the request is forwarded to the owner via a network event.
        /// </summary>
        public virtual void StopProcess()
        {
            if (!IsProcessRunning())
            {
                LogWarning("Process is not running, ignoring stop call.");
                return;
            }

            if (!IsProcessOwner())
            {
                SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RequestStopProcess));
                return;
            }

            ExecuteStop();
        }

        [NetworkCallable(maxEventsPerSecond: 1)]
        public void RequestStopProcess()
        {
            if (!IsProcessOwner() || !IsProcessRunning())
            {
                LogWarning("RequestStopProcess rejected: not the owner or not running.");
                return;
            }

            ExecuteStop();
        }

        private void ExecuteStop()
        {
            _isRunning = false;
            OnProcessStopped();
            InternalCleanup(false);
        }

        /// <summary>
        /// Completes the Process successfully.
        /// If called by a non-owner, the request is forwarded to the owner via a network event.
        /// </summary>
        public virtual void CompleteProcess()
        {
            if (!IsProcessRunning())
            {
                LogWarning("Process is not running, ignoring complete call.");
                return;
            }

            if (!IsProcessOwner())
            {
                SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RequestCompleteProcess));
                return;
            }

            ExecuteComplete();
        }

        [NetworkCallable(maxEventsPerSecond: 1)]
        public void RequestCompleteProcess()
        {
            if (!IsProcessOwner() || !IsProcessRunning())
            {
                LogWarning("RequestCompleteProcess rejected: not the owner or not running.");
                return;
            }

            ExecuteComplete();
        }

        private void ExecuteComplete()
        {
            _isRunning = false;
            OnProcessCompleted();
            InternalCleanup(true);
        }

        /// <summary>
        /// Schedules the tick loop if it isn't already running.
        /// </summary>
        private void StartTickLoopIfNeeded()
        {
            if (_updateLoopActive) return;

            _updateLoopActive = true;
            _nextTickDueAtRealTime = Time.realtimeSinceStartup;
            _nextResyncDueAtRealTime = Time.realtimeSinceStartup + _autoResyncInterval;
            SendCustomEventDelayedSeconds(nameof(_TickProcessUpdate), 0f);
        }

        /// <summary>
        /// Runs one tick: drives the resync heartbeat and, if enabled, <see cref="OnProcessUpdate"/>.
        /// </summary>
        /// <remarks>
        /// Internal only, public only because <c>SendCustomEventDelayedSeconds</c> requires a
        /// public target. Never call this directly outside <see cref="Process"/>.
        /// </remarks>
        public void _TickProcessUpdate()
        {
            if (!_updateLoopActive)
            {
                LogWarning("Discarding stale tick call from an already-stopped loop.");
                return;
            }

            // Scheduled calls can't be canceled, so a stop+restart in the same frame can leave a
            // stale tick pending that _updateLoopActive alone can't detect; this deadline check does.
            if (Time.realtimeSinceStartup < _nextTickDueAtRealTime) return;

            // Stop the loop if the process ended or ownership was transferred away.
            if (!IsProcessRunning() || !IsProcessOwner())
            {
                _updateLoopActive = false;
                _ownershipEstablished = false;
                return;
            }

            if (_useProcessUpdate)
            {
                OnProcessUpdate();
            }

            // Re-check since OnProcessUpdate() could have stopped the process or ownership.
            if (!IsProcessRunning() || !IsProcessOwner())
            {
                _updateLoopActive = false;
                _ownershipEstablished = false;
                return;
            }

            // Just in case a discrete event silently failed to deliver: re-broadcasts every
            // synced field (subclass ones included) on its own, coarser cadence.
            if (Time.realtimeSinceStartup >= _nextResyncDueAtRealTime)
            {
                RequestSerialization();
                _nextResyncDueAtRealTime = Time.realtimeSinceStartup + _autoResyncInterval;
            }

            _nextTickDueAtRealTime = Time.realtimeSinceStartup + _processUpdateInterval;
            SendCustomEventDelayedSeconds(nameof(_TickProcessUpdate), _processUpdateInterval);
        }


        /// <summary>Returns <c>true</c> if the process is currently running.</summary>
        public bool IsProcessRunning()
        {
            return _isRunning;
        }

        /// <summary>
        /// Transfers process ownership to <paramref name="newOwner"/> and syncs the change
        /// to all clients. Prefer this over <c>Networking.SetOwner</c> directly.
        /// </summary>
        /// <remarks>
        /// Don't write synced fields right after this call: the old owner needs to ack the
        /// transfer first, or the write can silently drop.
        /// <c>https://udonsharp.docs.vrchat.com/networking-tips-&amp;-tricks/#known-issues</c>
        /// </remarks>
        protected void SetProcessOwner(VRCPlayerApi newOwner)
        {
            Networking.SetOwner(newOwner, gameObject);
        }

        /// <summary>
        /// Returns <c>true</c> if the local player is the current process owner.
        /// </summary>
        protected bool IsProcessOwner()
        {
            return Networking.IsOwner(gameObject);
        }

        /// <summary>
        /// Called after the process has started.
        /// </summary>
        /// <remarks>
        /// This runs only on the process owner, and before the first network packet goes out, so
        /// any synced fields you set here are included in that packet.
        /// </remarks>
        protected virtual void OnProcessStarted() { }

        /// <summary>
        /// Called when the process is stopped before it completes.
        /// </summary>
        /// <remarks>
        /// This runs only on the process owner, and only for a stop. A successful completion
        /// calls <see cref="OnProcessCompleted"/> instead, never this method.
        /// </remarks>
        protected virtual void OnProcessStopped() { }

        /// <summary>
        /// Called when the process finishes successfully.
        /// </summary>
        /// <remarks>
        /// This runs only on the process owner, and only for a successful completion. A forced
        /// stop calls <see cref="OnProcessStopped"/> instead, never this method.
        /// </remarks>
        protected virtual void OnProcessCompleted() { }

        /// <summary>
        /// Called when the process owner leaves the instance and the process is still running.
        /// <b>Only invoked on the new owner.</b>
        /// </summary>
        protected virtual void OnOwnerAbandonedProcess() { }

        /// <summary>
        /// Called when a cleanup of the process data is requested.
        /// <b>Only invoked on the process owner.</b>
        /// Override to add subclass-specific cleanup. No base call required.
        /// Mandatory state resets and serialization are handled by <see cref="InternalCleanup"/>.
        /// </summary>
        /// <param name="isCompleted">True if cleanup is after successful completion, false if after stop/abort.</param>
        protected virtual void OnProcessCleanup(bool isCompleted) { }

        /// <summary>
        /// Clears synced process state, calls the <see cref="OnProcessCleanup"/> subclass hook,
        /// then serializes everything in one packet. Always called by <see cref="ExecuteStop"/>
        /// and <see cref="ExecuteComplete"/> so critical resets are never skipped even if a
        /// subclass overrides <see cref="OnProcessCleanup"/> without calling base.
        /// </summary>
        private void InternalCleanup(bool isCompleted)
        {
            // If a listener reacts to OnProcessStopped or OnProcessCompleted by calling
            // StartProcess synchronously (which is possible because VRChat fires
            // SendCustomNetworkEvent(All,...) inline on the sender), _isRunning will be true
            // again by the time we reach this point. In that case _ownerId, _useProcessUpdate,
            // and _updateLoopActive already belong to the new process and must not be cleared.
            // Clearing _ownerId would make IsProcessOwner() return false, causing subclass
            // broadcast handlers to reject all incoming acks. Clearing _updateLoopActive would
            // kill the new process tick loop on the next invocation.
            //
            // OnProcessCleanup is skipped in that same case, for the same reason: it
            // belongs to the process that just stopped/completed, and subclasses are
            // documented to clear their own synced state inside it. Running it here
            // would clobber the new process's just-set state one line after its own
            // OnProcessStarted() already fired.
            if (!_isRunning)
            {
                _tsOwnerId = "";
                _ownerPlayerIdInt = 0;
                _useProcessUpdate = false;
                _updateLoopActive = false;

                // Call OnProcessCleanup before RequestSerialization so that any synced
                // variables a subclass clears in that hook are already zeroed when the
                // packet goes out. If we serialized first, remote clients would receive
                // _isRunning=false alongside stale subclass array values, and unless the
                // subclass calls RequestSerialization itself in OnProcessCleanup, no
                // corrective packet would ever follow.
                OnProcessCleanup(isCompleted);
            }

            // Serialize the final state so remote clients see the cleared owner and running flag.
            // If a subclass already called RequestSerialization inside OnProcessCleanup, this
            // second call is harmless. If a new process was started from an inline callback,
            // _isRunning is true and we serialize the new process state instead.
            RequestSerialization();
        }

        /// <summary>
        /// Called at regular intervals if the process is running and the local player is the owner
        /// and <c>useProcessUpdate: true</c> was passed to <see cref="StartProcess"/>.
        /// <b>Only invoked on the process owner.</b>
        /// </summary>
        protected virtual void OnProcessUpdate() { }

        private void TakeOverAbandonedProcess()
        {
            if (_ownershipEstablished) return;
            _ownershipEstablished = true;

            // Fires before the tick loop starts, same ordering as StartProcess/OnProcessStarted.
            OnOwnerAbandonedProcess();

            StartTickLoopIfNeeded();
        }
    }
}