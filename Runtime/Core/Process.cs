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
        // Bumped once per run in ExecuteProcessStart, never reset, so a stale Request*Process call gets caught.
        [UdonSynced] private int _runGeneration = 0;

        // Blocks TakeOverRunningProcess from re-firing once ownership is settled
        private bool _ownershipEstablished = false;

        private const float _processUpdateInterval = 0.5f;
        // Tradeoff: short enough that a missed event is only briefly wrong; long enough to stay cheap.
        private const float _autoResyncInterval = 5f;
        // Gates the tick loop: OnProcessUpdate and the resync heartbeat.
        private bool _updateLoopActive = false;
        private float _nextTickDueAtRealTime = 0f;
        // Separate from _nextTickDueAtRealTime since the two run on different cadences.
        private float _nextResyncDueAtRealTime = 0f;

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            base.OnPlayerLeft(player);

            if (!IsProcessRunning() || !IsProcessOwner()) return;

            TakeOverRunningProcess();
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            base.OnOwnershipTransferred(player);

            // Backstop for OnPlayerLeft, in case it ran before ownership actually landed.
            if (!IsProcessRunning() || !IsProcessOwner()) return;

            TakeOverRunningProcess();
        }

        public override void OnPlayerSuspendChanged(VRCPlayerApi player)
        {
            base.OnPlayerSuspendChanged(player);

            // A suspended device stops running Udon and stops responding to network events:
            // https://creators.vrchat.com/worlds/udon/players/#get-issuspended
            // If the owner suspends, the tick loop stalls with nobody left able to restart it.
            if (!player.isSuspended || !IsProcessRunning() || !IsProcessOwnedBy(player)) return;

            SetProcessOwner(Networking.LocalPlayer);
        }

        private void TakeOverRunningProcess()
        {
            if (_ownershipEstablished) return;
            _ownershipEstablished = true;

            // Fires before the tick loop starts, same ordering as StartProcess/OnProcessStarted.
            OnBecameProcessOwner();

            StartTickLoopIfNeeded();
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
        /// Starts the Process. Safe to call from anyone: if the local player is not the owner, this just
        /// asks the owner to start it.
        /// </summary>
        /// <remarks>
        /// Does not change who owns the object. If you want the local player to become the
        /// owner, call <see cref="SetProcessOwner"/> yourself before calling this.
        /// </remarks>
        public virtual void StartProcess()
        {
            if (IsProcessRunning())
            {
                LogWarning("Process is already running, ignoring start call.");
                return;
            }

            if (!IsProcessOwner())
            {
                SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RequestStartProcess));
                return;
            }

            ExecuteProcessStart();
        }

        [NetworkCallable(maxEventsPerSecond: 1)]
        public void RequestStartProcess()
        {
            if (!IsProcessOwner() || IsProcessRunning())
            {
                LogWarning("RequestStartProcess rejected: not the owner or already running.");
                return;
            }

            ExecuteProcessStart();
        }

        protected void ExecuteProcessStart()
        {
            _isRunning = true;
            _ownershipEstablished = true;
            _runGeneration++;

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
                SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RequestStopProcess), _runGeneration);
                return;
            }

            ExecuteProcessStop();
        }

        [NetworkCallable(maxEventsPerSecond: 1)]
        public void RequestStopProcess(int requestedGeneration)
        {
            if (!IsProcessOwner())
            {
                LogWarning("RequestStopProcess rejected: not the owner.");
                return;
            }

            if (!IsProcessRunning())
            {
                LogWarning("RequestStopProcess rejected: not running.");
                return;
            }

            if (requestedGeneration != _runGeneration)
            {
                LogWarning("RequestStopProcess rejected: stale run.");
                return;
            }

            ExecuteProcessStop();
        }

        protected void ExecuteProcessStop()
        {
            _isRunning = false;
            OnProcessStopped();
            ExecuteCleanup(false);
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
                SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RequestCompleteProcess), _runGeneration);
                return;
            }

            ExecuteProcessComplete();
        }

        [NetworkCallable(maxEventsPerSecond: 1)]
        public void RequestCompleteProcess(int requestedGeneration)
        {
            if (!IsProcessOwner())
            {
                LogWarning("RequestCompleteProcess rejected: not the owner.");
                return;
            }

            if (!IsProcessRunning())
            {
                LogWarning("RequestCompleteProcess rejected: not running.");
                return;
            }

            if (requestedGeneration != _runGeneration)
            {
                LogWarning("RequestCompleteProcess rejected: stale run.");
                return;
            }

            ExecuteProcessComplete();
        }

        protected void ExecuteProcessComplete()
        {
            _isRunning = false;
            OnProcessCompleted();
            ExecuteCleanup(true);
        }

        private void ExecuteCleanup(bool isCompleted)
        {
            if (!IsProcessRunning())
            {
                _updateLoopActive = false;
                _ownershipEstablished = false;

                OnProcessCleanup(isCompleted);
            }

            RequestSerialization();
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
        /// Runs one tick: calls <see cref="OnProcessUpdate"/> and drives the resync heartbeat.
        /// </summary>
        /// <remarks>
        /// Internal only, public only because <c>SendCustomEventDelayedSeconds</c> requires a
        /// public target. Never call this directly, including from subclasses.
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

            OnProcessUpdate();

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
        /// Returns <c>true</c> if <paramref name="player"/> is the current process owner.
        /// </summary>
        protected bool IsProcessOwnedBy(VRCPlayerApi player)
        {
            return Networking.IsOwner(player, gameObject);
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
        /// Called when the local player becomes the new owner of a process that was already
        /// running under a different owner.
        /// </summary>
        /// <remarks>
        /// This can happen because the previous owner left the instance, but also for an
        /// intentional handoff, such as the explicit <c>SetOwner</c> call this class makes when
        /// the previous owner's client is suspended. Runs once, before the tick loop restarts,
        /// even though more than one VRChat callback can each attempt to detect the same handoff.
        /// </remarks>
        protected virtual void OnBecameProcessOwner() { }

        /// <summary>
        /// Called while the process is being cleaned up after a stop or a completion. Override
        /// this to clear any synced state your subclass owns.
        /// </summary>
        /// <remarks>
        /// Runs only on the process owner, before the cleanup packet is serialized, so state you
        /// clear here is reflected in that packet.
        /// </remarks>
        /// <param name="isCompleted">True if cleanup is after successful completion, false if after stop/abort.</param>
        protected virtual void OnProcessCleanup(bool isCompleted) { }

        /// <summary>
        /// Called on a fixed interval while the process is running.
        /// </summary>
        /// <remarks>
        /// Runs only on the process owner, roughly every half second, until the process ends or
        /// the local player stops being the owner.
        /// </remarks>
        protected virtual void OnProcessUpdate() { }
    }
}