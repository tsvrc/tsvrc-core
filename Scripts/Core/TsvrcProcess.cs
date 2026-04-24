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

        // Cached local player ID, constant for the session. Protected so subclasses can use it
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
            // Stale-packet guard: in Manual sync mode a reliable packet sent by the departed
            // or suspended owner can arrive after TakeOverAbandonedProcess has already run,
            // overwriting _ownerId with the old owner's ID. Two cascading failures result:
            //   (a) _TickProcessUpdate sees IsProcessOwner()=false and kills the tick loop.
            //   (b) A subsequent OnOwnershipTransferred (from any cause) sees IsProcessOwner()=
            //       false + FindPlayerByID(_ownerId)=null and re-enters TakeOverAbandonedProcess
            //       spuriously, even when _useProcessUpdate=false (where _updateLoopActive is
            //       never set, so a guard keyed on that flag would never fire for that case).
            //
            // The fix: if we are the Unity owner, the process is still marked running, and
            // _ownerId names a player who has already left or is suspended, re-assert ownership.
            // This mirrors the same FindPlayerByID pattern in OnOwnershipTransferred.
            //
            // Per VRChat docs: departed players are removed from GetPlayers() before OnPlayerLeft
            // fires, so FindPlayerByID returns null for them. Suspended players remain in the
            // instance with isSuspended=true.
            if (Networking.IsOwner(gameObject) && _isRunning && !IsProcessOwner())
            {
                // _ownerId="" is the "no owner" sentinel; a running process should never have
                // an empty owner. Guards against FindPlayerByID("") returning null and spuriously
                // claiming ownership. Mirrors the identical guard in OnOwnershipTransferred.
                if (_ownerId == "") return;

                var namedOwner = TsPlayer.FindPlayerByID(_ownerId);
                if (namedOwner == null || namedOwner.isSuspended)
                {
                    // No return after SetProcessOwner: fall through to the loop-restart block
                    // below so that if the stale packet killed the loop (_TickProcessUpdate saw
                    // IsProcessOwner()=false and set _updateLoopActive=false before this
                    // OnDeserialization fired), the loop is recovered in the same event.
                    SetProcessOwner(Networking.LocalPlayer);
                }
            }

            // Restart the update loop if we are the owner and it is not already running.
            // Covers stale-packet recovery (loop was killed before re-assertion above) and any
            // other scenario where the loop should be running but isn't. Does NOT fire on the
            // sender of RequestSerialization (VRChat guarantee), so TakeOverAbandonedProcess
            // restarts the loop explicitly to cover that gap.
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
        /// <remarks>
        /// <b>Concurrent-start race:</b> if two clients call <c>StartProcess</c> before either's
        /// <c>RequestSerialization</c> packet has been received, both will pass the
        /// <c>_isRunning</c> guard and both will call <c>OnProcessStarted</c>. Eventually one
        /// client's packet overwrites the other via <c>OnDeserialization</c>, leaving the losing
        /// client with a locally-running process that the network has discarded. Callers should
        /// guard against concurrent starts at a higher level (e.g. call only from the master or
        /// via a coordinated network event).
        /// </remarks>
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
            // the previous owner just left and VRChat re-assigned Unity ownership to us before
            // the new _ownerId packet arrives). Avoids the unnecessary network round-trip of
            // self-sending the event via NetworkEventTarget.Owner.
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

            // Same dual-authority check as StopProcess; see its comment.
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
        /// The <c>_ownerId != ""</c> guard prevents a false positive when both fields are
        /// <c>""</c>: <c>_ownerId = ""</c> is the "no owner" sentinel (process not running),
        /// and if <c>TsConstruct</c> was never called <c>_localPlayerId</c> is also <c>""</c>,
        /// which would otherwise make this return <c>true</c> even with no active process.
        /// </remarks>
        protected bool IsProcessOwner()
        {
            return _ownerId != "" && _ownerId == _localPlayerId;
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
            // clear them, leaving remote clients with _isRunning=false but dirty synced arrays
            // that are never corrected (subclasses that don't call their own RequestSerialization
            // in OnProcessCleanup would never send a corrective packet).
            OnProcessCleanup(isCompleted);

            // Serialize the cleared owner/state so remote clients don't see stale data.
            // If a subclass also called RequestSerialization() inside OnProcessCleanup, this
            // second call is redundant but harmless; both packets carry fully-cleared state.
            RequestSerialization();
        }

        /// <summary>
        /// Called at regular intervals if the process is running and the local player is the owner.
        /// <b>Only invoked on the process owner.</b>
        /// </summary>
        protected virtual void OnProcessUpdate() { }

        // Public only because SendCustomEventDelayedSeconds requires a public method target;
        // do not call this directly.
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
        /// <remarks>
        /// Rate-limited to 1/s. Any player in the instance can invoke this directly as a
        /// network event (VRChat cannot restrict callers of <c>[NetworkCallable]</c> methods).
        /// Authorization beyond the owner-guard below is the responsibility of subclasses.
        /// </remarks>
        [NetworkCallable(maxEventsPerSecond: 1)]
        public void RequestStopProcess()
        {
            // Accept on either authority:
            //   IsProcessOwner() for the normal case where _ownerId deserialization has already arrived.
            //   Networking.IsOwner() as a fallback for the race where the previous owner just left,
            //   VRChat re-routed this event to the new Unity owner, but the deserialization packet
            //   carrying the new _ownerId has not arrived yet, so IsProcessOwner() is still false.
            if ((!IsProcessOwner() && !Networking.IsOwner(gameObject)) || !_isRunning) return;
            ExecuteStop();
        }

        /// <summary>
        /// Received by the owner when a non-owner calls <see cref="CompleteProcess"/>.
        /// Guard checks discard the event if ownership disagreement caused misrouting,
        /// or if the process already completed before the packet arrived.
        /// </summary>
        /// <remarks>
        /// Rate-limited to 1/s. Same caller-authorization note as <see cref="RequestStopProcess"/>.
        /// </remarks>
        [NetworkCallable(maxEventsPerSecond: 1)]
        public void RequestCompleteProcess()
        {
            // Same dual-authority guard as RequestStopProcess; see its comment.
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