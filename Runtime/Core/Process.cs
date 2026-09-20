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
                _StartTickLoopIfNeeded();
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

            _StartTickLoopIfNeeded();
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

        // Schedules the tick loop if it is not already running. The loop always runs for any
        // running, owned process - it drives the resync heartbeat (see the class remarks)
        // unconditionally, and additionally calls OnProcessUpdate every tick if a subclass opted
        // into that via StartProcess(useProcessUpdate: true).
        //
        // !_updateLoopActive matters when a caller (e.g. OnProcessStarted) reentrantly stops and
        // restarts the process: the inner StartProcess call already schedules a tick and sets
        // this flag, so without the guard an outer call would schedule a redundant second one.
        private void _StartTickLoopIfNeeded()
        {
            if (_updateLoopActive) return;

            // A 0-second delay defers the first tick to the next frame so that the caller
            // returns before OnProcessUpdate fires. This prevents re-entrancy and lets callers
            // set up additional state after starting the process.
            // SendCustomEventDelayedSeconds is local-only and never routed over the network.
            _updateLoopActive = true;
            _nextTickDueAtRealTime = Time.realtimeSinceStartup;
            _nextResyncDueAtRealTime = Time.realtimeSinceStartup + _autoResyncInterval;
            SendCustomEventDelayedSeconds(nameof(_TickProcessUpdate), 0f);
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
        /// https://udonsharp.docs.vrchat.com/networking-tips-&-tricks#known-issues
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
        /// Called when the process is started.
        /// <b>Only invoked on the process owner.</b>
        /// </summary>
        protected virtual void OnProcessStarted() { }

        /// <summary>
        /// Called when the process is stopped before completion.
        /// <b>Only invoked on the process owner.</b>
        /// </summary>
        protected virtual void OnProcessStopped() { }

        /// <summary>
        /// Called when the process is completed successfully.
        /// <b>Only invoked on the process owner.</b>
        /// </summary>
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

        // Public only because SendCustomEventDelayedSeconds requires a public method target.
        // Do not call this directly.
        //
        // Deliberately still SendCustomEventDelayedSeconds-based, not Update()-based: this loop
        // must keep ticking while its GameObject is inactive (e.g. a hidden UI representation of
        // a still-running process), and Unity's automatic Update() message is never delivered to
        // an inactive GameObject or disabled component, a hard engine constraint, not something
        // any C#/UdonSharp-level design can opt out of. SendCustomEventDelayedSeconds is tracked
        // by Udon's own scheduler instead of Unity's native per-frame component dispatch, so it
        // keeps firing regardless of GameObject activity.
        //
        // Runs for every running, owned process, regardless of _useProcessUpdate: it always
        // drives the resync heartbeat (see the class remarks), and additionally calls
        // OnProcessUpdate on ticks where a subclass opted into that.
        public void _TickProcessUpdate()
        {
            // If InternalCleanup set _updateLoopActive to false, this call belongs to a loop that
            // was fully stopped with no restart. Discard it here without touching the flag so a
            // subsequent StartProcess can safely schedule a new loop.
            if (!_updateLoopActive) return;

            // SendCustomEventDelayedSeconds cannot be canceled once scheduled, so calling
            // StopProcess() then StartProcess() in the same frame can leave this exact stopped
            // loop's already-in-flight scheduled call pending even after a brand new loop has set
            // _updateLoopActive back to true (the check above alone can't tell the two loops
            // apart, since both are "active" as far as that single boolean is concerned).
            // _nextTickDueAtRealTime is advanced on every legitimate tick and on every activation
            // to reflect the CURRENT generation's expected cadence; a call that fires before that
            // real-time deadline belongs to an earlier, already-superseded generation and is
            // discarded here, deliberately without rescheduling, so a stale chain dies on its own
            // instead of retrying indefinitely.
            if (Time.realtimeSinceStartup < _nextTickDueAtRealTime) return;

            // Stop the loop if the process ended or ownership was transferred away.
            if (!IsProcessRunning() || !IsProcessOwner())
            {
                _updateLoopActive = false;
                return;
            }

            if (_useProcessUpdate) OnProcessUpdate();

            // Check again after the callback since OnProcessUpdate() could stop the process
            // or transfer ownership. Without this, any ownership change inside the callback
            // would leave the loop running for one extra tick.
            if (!IsProcessRunning() || !IsProcessOwner())
            {
                _updateLoopActive = false;
                return;
            }

            // Resync on its own, coarser cadence rather than every tick - see the class remarks
            // for why this exists. RequestSerialization re-broadcasts every synced field a
            // subclass declares, not just this class's own, so this alone recovers any subclass
            // state a discrete event failed to deliver.
            if (Time.realtimeSinceStartup >= _nextResyncDueAtRealTime)
            {
                RequestSerialization();
                _nextResyncDueAtRealTime = Time.realtimeSinceStartup + _autoResyncInterval;
            }

            _nextTickDueAtRealTime = Time.realtimeSinceStartup + _processUpdateInterval;
            SendCustomEventDelayedSeconds(nameof(_TickProcessUpdate), _processUpdateInterval);
        }

        private void TakeOverAbandonedProcess()
        {
            if (_ownershipEstablished) return;
            _ownershipEstablished = true;

            // Fires before the tick loop starts, same ordering as StartProcess/OnProcessStarted.
            OnOwnerAbandonedProcess();

            _StartTickLoopIfNeeded();
        }
    }
}