using Tsvrc.Player;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace Tsvrc.Core
{
    /// <summary>
    /// Base class for networked, owner-driven processes.
    /// Manages start/stop/complete lifecycle, ownership transfer on player leave,
    /// and optional periodic update ticks, all scoped to the process owner.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class TsvrcProcess : TsvrcBehaviour
    {
        [UdonSynced] private bool _isRunning = false;
        [UdonSynced] private string _ownerId = "";
        [UdonSynced] private bool _useProcessUpdate = false;

        private const float _processUpdateInterval = 0.5f;

        // Tracks whether the update loop is currently scheduled locally.
        // Prevents duplicate loops after ownership transfer.
        private bool _updateLoopActive = false;

        // Cached local player ID to avoid string construction on every IsProcessOwner() call.
        // The local player identity is constant for the duration of a session.
        private string _localPlayerId = "";

        protected override void TsStart()
        {
            base.TsStart();

            _localPlayerId = TsPlayer.GetPlayerID(Networking.LocalPlayer);
        }

        /// <summary>
        /// Resets this process to a clean state for reuse. Clears running state,
        /// event subscriptions, and the constructed flag. Deactivate the GameObject
        /// and call <see cref="TsvrcBehaviour.TsConstruct(Tsvrc.Core.Compiled.CompiledTsvrc)"/>
        /// again to reuse it.
        /// </summary>
        public virtual void TsRelease()
        {
            // Guard against double invocation: ExecuteStop/ExecuteComplete already called
            // OnProcessCleanup. A second call would pass a contradictory isCompleted value.
            if (_isRunning)
                OnProcessCleanup(false);

            ResetBehaviourState();
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (!IsProcessRunning()) return;

            var playerId = TsPlayer.GetPlayerID(player);
            if (playerId != _ownerId) return;

            // VRChat normally transfers Unity ownership before OnPlayerLeft fires, but a known
            // event-ordering bug can cause OnOwnershipTransferred to fire after OnPlayerLeft.
            // OnOwnershipTransferred handles that fallback.
            if (!Networking.IsOwner(gameObject)) return;

            TakeOverAbandonedProcess();
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            base.OnOwnershipTransferred(player);

            // 1. Fallback for the event-ordering bug: OnPlayerLeft may have found IsOwner=false
            //    and done nothing. FindPlayerByID confirms the owner is truly gone.
            // 2. IsProcessOwner() guard skips this if OnPlayerLeft already ran and
            //    TakeOverAbandonedProcess already updated _ownerId to our ID.
            if (!Networking.IsOwner(gameObject) || !IsProcessRunning() || IsProcessOwner()) return;
            if (_ownerId == "" || TsPlayer.FindPlayerByID(_ownerId) != null) return;

            TakeOverAbandonedProcess();
        }

        public override void OnDeserialization()
        {
            // Synced variables are guaranteed current here, the safe place to restart
            // the update loop after an ownership transfer. Guard against duplicate loops.
            if (IsProcessOwner() && IsProcessRunning() && _useProcessUpdate && !_updateLoopActive)
            {
                _updateLoopActive = true;
                SendCustomEvent(nameof(_TickProcessUpdate));
            }
        }

        public virtual void StartProcess(bool useProcessUpdate = false)
        {
            if (_isRunning)
            {
                Debug.LogWarning("[TsvrcProcess] Process is already running.");
                return;
            }

            // Set state before SetProcessOwner so its RequestSerialization sends a single
            // atomic packet with ownership and running state together.
            _isRunning = true;
            _useProcessUpdate = useProcessUpdate;

            if (!IsProcessOwner())
            {
                SetProcessOwner(Networking.LocalPlayer);
            }
            else
            {
                RequestSerialization();
            }

            OnProcessStarted();

            if (_useProcessUpdate)
            {
                // Caller is always the owner at this point. Use SendCustomEvent to avoid
                // routing through the network (which could hit the previous Unity owner if
                // Networking.SetOwner hasn't propagated yet).
                _updateLoopActive = true;
                SendCustomEvent(nameof(_TickProcessUpdate));
            }
        }

        /// <summary>
        /// Forcibly stops the Tsvrc Process before completion.
        /// If called by a non-owner, the request is forwarded to the owner via a network event.
        /// </summary>
        public virtual void StopProcess()
        {
            if (!_isRunning)
            {
                Debug.LogWarning("[TsvrcProcess] Process is not running.");
                return;
            }

            if (!IsProcessOwner())
            {
                // Forward to the owner; the guard in RequestStopProcess discards stale arrivals.
                SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RequestStopProcess));
                return;
            }

            ExecuteStop();
        }

        /// <summary>
        /// Completes the Tsvrc Process successfully.
        /// If called by a non-owner, the request is forwarded to the owner via a network event.
        /// </summary>
        public void CompleteProcess()
        {
            if (!_isRunning)
            {
                Debug.LogWarning("[TsvrcProcess] Process is not running.");
                return;
            }

            if (!IsProcessOwner())
            {
                SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RequestCompleteProcess));
                return;
            }

            ExecuteComplete();
        }

        public bool IsProcessRunning()
        {
            return _isRunning;
        }

        /// <summary>
        /// Transfers process ownership to <paramref name="newOwner"/> and syncs the change
        /// to all clients. Prefer this over <c>Networking.SetOwner</c> directly.
        /// </summary>
        protected void SetProcessOwner(VRCPlayerApi newOwner)
        {
            _ownerId = TsPlayer.GetPlayerID(newOwner);
            Networking.SetOwner(newOwner, gameObject);
            RequestSerialization();
        }

        /// <summary>
        /// Returns <c>true</c> if the local player is the current process owner.
        /// </summary>
        /// <remarks>
        /// Uses <c>_ownerId</c> instead of <c>Networking.IsOwner()</c> because
        /// <c>SetOwner</c> is not immediately reflected locally, whereas <c>_ownerId</c>
        /// is set synchronously and arrives atomically with <c>_isRunning</c> on remote clients.
        /// </remarks>
        protected bool IsProcessOwner()
        {
            return _ownerId == _localPlayerId;
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
        /// </summary>
        /// <param name="isCompleted">True if cleanup is after successful completion, false if after stop/abort.</param>
        protected virtual void OnProcessCleanup(bool isCompleted)
        {
            _isRunning = false;
            _ownerId = "";
            _useProcessUpdate = false;
            _updateLoopActive = false;

            // Serialize the cleared owner/state so remote clients don't see stale _ownerId,
            // which would cause IsProcessOwner() to incorrectly return true on the old owner.
            RequestSerialization();
        }

        /// <summary>
        /// Called at regular intervals if the process is running and the local player is the owner.
        /// <b>Only invoked on the process owner.</b>
        /// </summary>
        protected virtual void OnProcessUpdate() { }

        public void _TickProcessUpdate()
        {
            // Stop the loop if the process ended or ownership was transferred away.
            if (!IsProcessRunning() || !IsProcessOwner())
            {
                _updateLoopActive = false;
                return;
            }

            OnProcessUpdate();

            // Re-check after the callback: OnProcessUpdate() may have stopped the process
            // or transferred ownership. Without this, a SetProcessOwner() call inside the
            // callback would not stop the loop until the next invocation.
            if (!IsProcessRunning() || !IsProcessOwner())
            {
                _updateLoopActive = false;
                return;
            }

            SendCustomEventDelayedSeconds(nameof(_TickProcessUpdate), _processUpdateInterval);
        }

        /// <summary>
        /// Received by the owner when a non-owner calls <see cref="StopProcess"/>.
        /// Guard checks discard the event if ownership disagreement caused misrouting,
        /// or if the process already stopped before the packet arrived.
        /// </summary>
        [NetworkCallable]
        public void RequestStopProcess()
        {
            if (!IsProcessOwner() || !_isRunning) return;
            ExecuteStop();
        }

        /// <summary>
        /// Received by the owner when a non-owner calls <see cref="CompleteProcess"/>.
        /// </summary>
        [NetworkCallable]
        public void RequestCompleteProcess()
        {
            if (!IsProcessOwner() || !_isRunning) return;
            ExecuteComplete();
        }

        private void TakeOverAbandonedProcess()
        {
            SetProcessOwner(Networking.LocalPlayer);

            // OnDeserialization does not fire on the sender of RequestSerialization;
            // the loop must be restarted explicitly here.
            if (_useProcessUpdate && !_updateLoopActive)
            {
                _updateLoopActive = true;
                SendCustomEvent(nameof(_TickProcessUpdate));
            }

            OnOwnerAbandonedProcess();
        }

        private void ExecuteStop()
        {
            _isRunning = false;
            OnProcessStopped();
            OnProcessCleanup(false);
        }

        private void ExecuteComplete()
        {
            _isRunning = false;
            OnProcessCompleted();
            OnProcessCleanup(true);
        }
    }
}