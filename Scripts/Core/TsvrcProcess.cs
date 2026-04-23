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

        // Prevents duplicate loop scheduling after ownership transfer.
        private bool _updateLoopActive = false;

        // Cached local player ID — session-constant. Protected so subclasses can use it
        // directly instead of calling TsPlayer.GetPlayerID(Networking.LocalPlayer).
        protected string _localPlayerId = "";

        protected override void TsStart()
        {
            base.TsStart();

            _localPlayerId = TsPlayer.GetPlayerID(Networking.LocalPlayer);
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
            // 3. Also covers the OnPlayerSuspendChanged path: the old owner is still findable
            //    (suspended players stay in the instance), so we must also check isSuspended.
            if (!Networking.IsOwner(gameObject) || !IsProcessRunning() || IsProcessOwner()) return;
            if (_ownerId == "") return;

            var oldOwner = TsPlayer.FindPlayerByID(_ownerId);
            if (oldOwner != null && !oldOwner.isSuspended) return;

            TakeOverAbandonedProcess();
        }

        public override void OnPlayerSuspendChanged(VRCPlayerApi player)
        {
            // VRChat docs: "While suspended, devices don't run Udon code or respond to network
            // events until the player reopens VRChat." A suspended process owner would stall
            // the tick loop indefinitely. VRChat recommends transferring ownership of important
            // objects away from suspended players (see OnPlayerSuspendChanged docs).
            //
            // Only act on the suspend event (isSuspended=true), not the wakeup (isSuspended=false).
            // On wakeup the suspended player receives buffered deserialization packets that correct
            // their local _ownerId, so no action is needed.
            //
            // Multiple non-suspended clients all see this event simultaneously and all call
            // Networking.SetOwner. VRChat picks one winner; OnOwnershipTransferred then fires
            // for everyone, and only the actual new Unity owner runs TakeOverAbandonedProcess.
            if (!player.isSuspended || !IsProcessRunning()) return;
            if (TsPlayer.GetPlayerID(player) != _ownerId) return;
            if (Networking.IsOwner(gameObject)) return;

            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        public override void OnDeserialization()
        {
            // Synced variables are guaranteed current here, the safe place to restart
            // the update loop after an ownership transfer. Guard against duplicate loops.
            // Use SendCustomEventDelayedSeconds(0f) — not SendCustomEvent — to avoid running
            // _TickProcessUpdate (and therefore OnProcessUpdate) synchronously inside
            // VRChat's networking callback. OnDeserialization fires during the network-update
            // phase; firing OnProcessUpdate here could observe other synced variables that
            // haven't been applied yet by VRChat's internal deserialization loop.
            if (IsProcessOwner() && IsProcessRunning() && _useProcessUpdate && !_updateLoopActive)
            {
                _updateLoopActive = true;
                SendCustomEventDelayedSeconds(nameof(_TickProcessUpdate), 0f);
            }
        }

        /// <summary>
        /// Starts the process, claiming ownership of the object for the local player.
        /// </summary>
        /// <param name="useProcessUpdate">When <c>true</c>, <see cref="OnProcessUpdate"/> fires every 0.5 s while the process runs.</param>
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
                // Use SendCustomEventDelayedSeconds (local-only, never routed over the network)
                // with a 0-second delay so the first tick is deferred to the next frame.
                // This ensures StartProcess fully returns to the caller before OnProcessUpdate
                // fires, preventing re-entrancy and letting callers set up state after this call.
                _updateLoopActive = true;
                SendCustomEventDelayedSeconds(nameof(_TickProcessUpdate), 0f);
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

            // Dual-authority check mirrors RequestStopProcess: accept either _ownerId match
            // (normal case) or Unity ownership (fallback for the deserialization-lag race where
            // the previous owner just left and VRChat re-assigned Unity ownership to us before the
            // new _ownerId packet arrives). Without the Networking.IsOwner() branch we would
            // self-send a network event to ourselves — which VRChat executes synchronously and
            // correctly (see docs: "it will trigger locally before moving on"), but going through
            // the network dispatch unnecessarily sets NetworkCalling.InNetworkCall=true for the
            // duration of ExecuteStop and its callbacks, visible to subclass overrides.
            if (!IsProcessOwner() && !Networking.IsOwner(gameObject))
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

            // Same dual-authority check as StopProcess — see its comment.
            if (!IsProcessOwner() && !Networking.IsOwner(gameObject))
            {
                SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RequestCompleteProcess));
                return;
            }

            ExecuteComplete();
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
        /// Override to add subclass-specific cleanup. No base call required.
        /// Mandatory state resets and serialization are handled by <see cref="InternalCleanup"/>.
        /// </summary>
        /// <param name="isCompleted">True if cleanup is after successful completion, false if after stop/abort.</param>
        protected virtual void OnProcessCleanup(bool isCompleted) { }

        /// <summary>
        /// Unconditionally clears synced process state, invokes the
        /// <see cref="OnProcessCleanup"/> subclass hook, then serializes everything to remotes
        /// in one atomic packet.
        /// Always called by <see cref="ExecuteStop"/> and <see cref="ExecuteComplete"/> so
        /// critical resets are never skipped even if a subclass overrides
        /// <see cref="OnProcessCleanup"/> without calling base.
        /// </summary>
        private void InternalCleanup(bool isCompleted)
        {
            _ownerId = "";
            _useProcessUpdate = false;
            _updateLoopActive = false;

            // Invoke the subclass hook BEFORE serializing so that any [UdonSynced] variables
            // a subclass clears in OnProcessCleanup (e.g. _trackedPlayerIds, _readyPlayerIds)
            // are already zeroed-out when RequestSerialization sends the packet. Without this
            // ordering, the first packet would carry stale subclass variable values because
            // InternalCleanup's RequestSerialization fired before the subclass had a chance to
            // clear them — leaving remote clients with _isRunning=false but dirty synced arrays
            // that are never corrected (subclasses that don't call their own RequestSerialization
            // in OnProcessCleanup would never send a corrective packet).
            OnProcessCleanup(isCompleted);

            // Serialize the cleared owner/state so remote clients don't see stale data.
            // If a subclass also called RequestSerialization() inside OnProcessCleanup, this
            // second call is redundant but harmless — both packets carry fully-cleared state.
            RequestSerialization();
        }

        /// <summary>
        /// Called at regular intervals if the process is running and the local player is the owner.
        /// <b>Only invoked on the process owner.</b>
        /// </summary>
        protected virtual void OnProcessUpdate() { }

        public void _TickProcessUpdate()
        {
            // Discard stale ticks that were already queued via SendCustomEventDelayedSeconds
            // when InternalCleanup (triggered by StopProcess/CompleteProcess) set
            // _updateLoopActive to false. Returning without modifying _updateLoopActive leaves
            // the field correctly false so a subsequent StartProcess can safely schedule a new loop.
            // Note: if StopProcess and StartProcess are called in the same frame, a stale tick
            // from the old loop may still reach here with _updateLoopActive=true (set by the new
            // StartProcess). That edge case cannot be fully resolved in UdonSharp without closures.
            if (!_updateLoopActive) return;

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
            // Accept on either authority:
            // - IsProcessOwner(): normal case — _ownerId deserialization has already arrived.
            // - Networking.IsOwner(): fallback for the race where the previous owner just left,
            //   VRChat re-routed this event to the new Unity owner, but the deserialization packet
            //   carrying the new _ownerId hasn't arrived yet, so IsProcessOwner() is still false.
            if ((!IsProcessOwner() && !Networking.IsOwner(gameObject)) || !_isRunning) return;
            ExecuteStop();
        }

        /// <summary>
        /// Received by the owner when a non-owner calls <see cref="CompleteProcess"/>.
        /// Guard checks discard the event if ownership disagreement caused misrouting,
        /// or if the process already completed before the packet arrived.
        /// </summary>
        [NetworkCallable]
        public void RequestCompleteProcess()
        {
            // Same dual-authority guard as RequestStopProcess — see its comment.
            if ((!IsProcessOwner() && !Networking.IsOwner(gameObject)) || !_isRunning) return;
            ExecuteComplete();
        }

        private void TakeOverAbandonedProcess()
        {
            SetProcessOwner(Networking.LocalPlayer);

            // Fire the "setup" callback before the tick loop starts, consistent with
            // StartProcess calling OnProcessStarted() before scheduling the first tick.
            // Subclasses that initialize state in OnOwnerAbandonedProcess() would otherwise
            // see uninitialized values on the very first OnProcessUpdate() call.
            OnOwnerAbandonedProcess();

            // OnDeserialization does not fire on the sender of RequestSerialization;
            // the loop must be restarted explicitly here. Use SendCustomEventDelayedSeconds
            // (local-only) so the tick is deferred to the next frame, consistent with StartProcess.
            if (_useProcessUpdate && !_updateLoopActive)
            {
                _updateLoopActive = true;
                SendCustomEventDelayedSeconds(nameof(_TickProcessUpdate), 0f);
            }
        }

        private void ExecuteStop()
        {
            _isRunning = false;
            OnProcessStopped();
            InternalCleanup(false);
        }

        private void ExecuteComplete()
        {
            _isRunning = false;
            OnProcessCompleted();
            InternalCleanup(true);
        }
    }
}