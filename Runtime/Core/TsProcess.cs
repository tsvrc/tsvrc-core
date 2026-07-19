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
    public class TsProcess : TsBehaviour
    {
        // Covers the entire TsProcess subtree (PlayerTracker, ReadyCheckProcess,
        // etc) with a single override - none of them need their own.
        protected override bool IsTsvrcInternal => true;

        [UdonSynced] private bool _isRunning = false;
        // The full string ID of the current process owner, formatted as "DisplayName#playerId".
        // Only used for FindPlayerByID lookups. All equality comparisons use _ownerPlayerIdInt
        // instead to avoid string allocations.
        [UdonSynced] private string _ownerId = "";
        // The numeric player ID of the current process owner, synced alongside _ownerId.
        // Lets every client check ownership without building the "DisplayName#playerId" string.
        // Always updated together with _ownerId inside SetProcessOwner.
        [UdonSynced] private int _ownerPlayerIdInt = 0;
        [UdonSynced] private bool _useProcessUpdate = false;

        private const float _processUpdateInterval = 0.5f;

        // Tracks whether the update loop is currently scheduled to prevent scheduling it twice.
        private bool _updateLoopActive = false;
        // Real-time (Time.realtimeSinceStartup) deadline the next legitimate tick is due at.
        // See _TickProcessUpdate for how this is used to discard stale scheduled calls.
        private float _nextTickDueAtRealTime = 0f;

        // Set to true just before and cleared just after every SendCustomNetworkEvent(All, ...) call.
        // On the sending client VRChat fires the event inline before returning, so the broadcast
        // handler runs inside our own call stack. This flag lets the handler's CallingPlayer guard
        // know the inline execution is legitimate, even when CallingPlayer carries a value from an
        // outer event context. This is safe because Udon is single-threaded.
        protected bool _isBroadcasting = false;

        // The local player's string ID, cached once in TsStart. Accessible by subclasses to avoid
        // calling TsPlayer.GetPlayerID(Networking.LocalPlayer) on every event.
        protected string _localPlayerId = "";
        // The local player's numeric ID, cached once in TsStart. Used for allocation-free
        // comparisons against _ownerPlayerIdInt.
        private int _localPlayerIdInt = 0;

        protected override void TsStart()
        {
            base.TsStart();

            _localPlayerId = TsPlayer.GetPlayerID(Networking.LocalPlayer);
            _localPlayerIdInt = Networking.LocalPlayer.playerId;
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            base.OnPlayerLeft(player);

            if (!IsProcessRunning()) return;

            // Skip if this player is not the current process owner. Comparing ints instead of
            // strings avoids allocation; it is equivalent because player IDs are unique per session.
            if (player.playerId != _ownerPlayerIdInt) return;

            // VRChat normally transfers Unity ownership before OnPlayerLeft fires. There is a known
            // engine bug where OnOwnershipTransferred fires after OnPlayerLeft instead. If that
            // happens, OnOwnershipTransferred handles the takeover as a fallback.
            if (!Networking.IsOwner(gameObject)) return;

            TakeOverAbandonedProcess();
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            base.OnOwnershipTransferred(player);

            // This handles three cases. First, if OnPlayerLeft ran but found IsOwner=false and
            // did nothing, we check here whether the named owner is truly gone. Second, if
            // OnPlayerLeft already ran TakeOverAbandonedProcess, IsProcessOwner() returns true
            // and we skip this block entirely. Third, when the old owner is suspended they are
            // still findable in the instance, so we also check isSuspended below.
            if (!Networking.IsOwner(gameObject) || !IsProcessRunning() || IsProcessOwner()) return;
            if (_ownerId == "") return;

            var oldOwner = TsPlayer.FindPlayerByID(_ownerId);
            if (oldOwner != null && !oldOwner.isSuspended) return;

            TakeOverAbandonedProcess();
        }

        public override void OnPlayerSuspendChanged(VRCPlayerApi player)
        {
            base.OnPlayerSuspendChanged(player);

            // A suspended process owner cannot run Udon code or receive network events, so the
            // tick loop would stall permanently. We need to transfer ownership away from them.
            //
            // We only react to the suspend event (isSuspended=true). On wakeup, the player
            // receives buffered deserialization packets that restore the correct _ownerId, so
            // no action is needed on wakeup.
            //
            // All non-suspended clients see this event simultaneously and each calls
            // Networking.SetOwner. VRChat picks one winner. OnOwnershipTransferred then fires
            // for all clients and only the actual new Unity owner runs TakeOverAbandonedProcess.
            if (!player.isSuspended || !IsProcessRunning()) return;
            // Same int comparison as OnPlayerLeft.
            if (player.playerId != _ownerPlayerIdInt) return;
            // Already the Unity owner, nothing to do.
            if (Networking.IsOwner(gameObject)) return;

            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        public override void OnDeserialization()
        {
            base.OnDeserialization();

            // In Manual sync mode, a reliable packet queued by a departed or suspended owner
            // can arrive after TakeOverAbandonedProcess has already run, overwriting _ownerId
            // with the old owner's ID. Two problems follow from that:
            //   a) _TickProcessUpdate sees IsProcessOwner() as false and kills the tick loop.
            //   b) A later OnOwnershipTransferred sees IsProcessOwner() as false and
            //      FindPlayerByID returns null, causing TakeOverAbandonedProcess to run again.
            //
            // To recover: if we hold Unity ownership, the process is still running, but _ownerId
            // names a player who is gone or suspended, we reassert ownership here.
            // VRChat removes departed players from GetPlayers() before OnPlayerLeft fires, so
            // FindPlayerByID returns null for them. Suspended players remain with isSuspended=true.
            if (Networking.IsOwner(gameObject) && _isRunning && !IsProcessOwner())
            {
                // _ownerId="" is always written together with _ownerPlayerIdInt=0, so
                // IsProcessOwner() is already false and the loop-restart block below would
                // not fire anyway. We still return early to prevent FindPlayerByID("") from
                // returning null and triggering a spurious ownership claim.
                if (_ownerId == "") return;

                var namedOwner = TsPlayer.FindPlayerByID(_ownerId);
                if (namedOwner == null || namedOwner.isSuspended)
                {
                    // Intentionally no return. We fall through to the loop-restart block so
                    // that if the stale packet already killed the loop before this event fired,
                    // the loop gets recovered in the same OnDeserialization call.
                    SetProcessOwner(Networking.LocalPlayer);
                }
            }

            // Restart the update loop if we are the owner and it is not currently running.
            // This covers stale-packet recovery and any other situation where the loop went
            // silent unexpectedly. See TakeOverAbandonedProcess for the one case this block
            // can't reach on its own.
            if (IsProcessOwner() && IsProcessRunning() && _useProcessUpdate && !_updateLoopActive)
            {
                _updateLoopActive = true;
                _nextTickDueAtRealTime = Time.realtimeSinceStartup;
                SendCustomEventDelayedSeconds(nameof(_TickProcessUpdate), 0f);
            }
        }

        /// <summary>
        /// Starts the process, claiming ownership of the object for the local player.
        /// </summary>
        /// <param name="useProcessUpdate">When <c>true</c>, <see cref="OnProcessUpdate"/> fires every 0.5 s while the process runs.</param>
        /// <remarks>
        /// If two clients call <c>StartProcess</c> before either's <c>RequestSerialization</c>
        /// packet is received, both pass the <c>_isRunning</c> guard and both fire
        /// <c>OnProcessStarted</c>. One client's packet eventually overwrites the other via
        /// <c>OnDeserialization</c>, leaving the losing client with a locally running process
        /// the network has already discarded. Protect against this at a higher level, for example
        /// by only calling this from the instance master or through a coordinated network event.
        /// </remarks>
        public virtual void StartProcess(bool useProcessUpdate = false)
        {
            if (_isRunning)
            {
                LogWarning("Process is already running.");
                return;
            }

            // Set _isRunning and _useProcessUpdate before calling SetProcessOwner so the
            // RequestSerialization inside it sends one packet containing all updated state.
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

            // !_updateLoopActive matters when OnProcessStarted() reentrantly stops and restarts
            // the process (e.g. calling StopProcess() then StartProcess() again from its own
            // hook): the inner StartProcess call already schedules a tick and sets this flag, so
            // without the guard this outer call would schedule a redundant second one.
            if (_useProcessUpdate && !_updateLoopActive)
            {
                // A 0-second delay defers the first tick to the next frame so that StartProcess
                // returns to the caller before OnProcessUpdate fires. This prevents re-entrancy
                // and lets callers set up additional state after this call.
                // SendCustomEventDelayedSeconds is local-only and never routed over the network.
                _updateLoopActive = true;
                _nextTickDueAtRealTime = Time.realtimeSinceStartup;
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
                LogWarning("Process is not running.");
                return;
            }

            // We accept authority if we are either the process owner by ID or the Unity owner.
            // The Unity owner fallback covers a timing window where the previous owner just left,
            // VRChat transferred Unity ownership to us, but our _ownerId has not been updated yet
            // by the incoming deserialization packet. Without this fallback we would unnecessarily
            // forward the stop request back to ourselves over the network.
            if (!IsProcessOwner() && !Networking.IsOwner(gameObject))
            {
                // Not the owner, so forward to whoever currently owns the object.
                // RequestStopProcess has its own guard to discard stale arrivals.
                SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RequestStopProcess));
                return;
            }

            ExecuteStop();
        }

        /// <summary>
        /// Completes the Tsvrc Process successfully.
        /// If called by a non-owner, the request is forwarded to the owner via a network event.
        /// </summary>
        public virtual void CompleteProcess()
        {
            if (!_isRunning)
            {
                LogWarning("Process is not running.");
                return;
            }

            // Same dual-authority check as StopProcess.
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
            _ownerPlayerIdInt = newOwner.playerId;
            Networking.SetOwner(newOwner, gameObject);
            RequestSerialization();
        }

        /// <summary>
        /// Returns <c>true</c> if the local player is the current process owner.
        /// </summary>
        /// <remarks>
        /// We compare <c>_ownerPlayerIdInt</c> instead of calling <c>Networking.IsOwner()</c>
        /// because <c>Networking.SetOwner</c> is not reflected locally right away. Our own
        /// <c>_ownerPlayerIdInt</c> is written synchronously in <c>SetProcessOwner</c> and
        /// arrives together with <c>_isRunning</c> for remote clients via <c>OnDeserialization</c>.
        /// The <c>_ownerPlayerIdInt != 0</c> guard prevents a false positive when no process is
        /// running. 0 is used as the no-owner sentinel because valid VRChat player IDs start at 1.
        /// If <c>TsStart</c> was never called, <c>_localPlayerIdInt</c> is also 0, which would
        /// otherwise cause this to return <c>true</c> with no active process.
        /// </remarks>
        protected bool IsProcessOwner()
        {
            // Equivalent to: _ownerId != "" && _ownerId == _localPlayerId, without the
            // string allocation.
            return _ownerPlayerIdInt != 0 && _ownerPlayerIdInt == _localPlayerIdInt;
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
                _ownerId = "";
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
        /// Called at regular intervals if the process is running and the local player is the owner.
        /// <b>Only invoked on the process owner.</b>
        /// </summary>
        protected virtual void OnProcessUpdate() { }

        // Public only because SendCustomEventDelayedSeconds requires a public method target.
        // Do not call this directly.
        //
        // Deliberately still SendCustomEventDelayedSeconds-based, not Update()-based: this loop
        // must keep ticking while its GameObject is inactive (e.g. a hidden UI representation of
        // a still-running process), and Unity's automatic Update() message is never delivered to
        // an inactive GameObject or disabled component — a hard engine constraint, not something
        // any C#/UdonSharp-level design can opt out of. SendCustomEventDelayedSeconds is tracked
        // by Udon's own scheduler instead of Unity's native per-frame component dispatch, so it
        // keeps firing regardless of GameObject activity.
        public void _TickProcessUpdate()
        {
            // If InternalCleanup set _updateLoopActive to false, this call belongs to a loop that
            // was fully stopped with no restart — discard it here without touching the flag so a
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
            // discarded here — deliberately without rescheduling, so a stale chain dies on its own
            // instead of retrying indefinitely.
            if (Time.realtimeSinceStartup < _nextTickDueAtRealTime) return;

            // Stop the loop if the process ended or ownership was transferred away.
            if (!IsProcessRunning() || !IsProcessOwner())
            {
                _updateLoopActive = false;
                return;
            }

            OnProcessUpdate();

            // Check again after the callback since OnProcessUpdate() could stop the process
            // or transfer ownership. Without this, any ownership change inside the callback
            // would leave the loop running for one extra tick.
            if (!IsProcessRunning() || !IsProcessOwner())
            {
                _updateLoopActive = false;
                return;
            }

            _nextTickDueAtRealTime = Time.realtimeSinceStartup + _processUpdateInterval;
            SendCustomEventDelayedSeconds(nameof(_TickProcessUpdate), _processUpdateInterval);
        }

        /// <summary>
        /// Received by the owner when a non-owner calls <see cref="StopProcess"/>.
        /// Guard checks discard the event if ownership disagreement caused misrouting,
        /// or if no process is running at all by the time the packet arrives.
        /// </summary>
        /// <remarks>
        /// Rate-limited to 1 call per second. Any player in the instance can invoke this directly
        /// as a network event because VRChat cannot restrict callers of <c>[NetworkCallable]</c>
        /// methods. Authorization beyond the owner guard below is the responsibility of subclasses.
        /// Calls beyond the rate limit are queued, not dropped, and can arrive up to roughly a
        /// second late. There is no per-run generation token, so a stale call arriving after the
        /// same owner has already stopped and restarted the process (a new, unrelated run) is
        /// indistinguishable from a legitimate call for the current run, and incorrectly stops it.
        /// Callers that stop and restart in quick succession need to coordinate that at a higher
        /// level, the same way <see cref="StartProcess"/>'s own remarks require for its race.
        /// </remarks>
        [NetworkCallable(maxEventsPerSecond: 1)]
        public void RequestStopProcess()
        {
            // Same dual-authority fallback as StopProcess, for the same timing window.
            if ((!IsProcessOwner() && !Networking.IsOwner(gameObject)) || !_isRunning) return;
            ExecuteStop();
        }

        /// <summary>
        /// Received by the owner when a non-owner calls <see cref="CompleteProcess"/>.
        /// Guard checks discard the event if ownership disagreement caused misrouting,
        /// or if no process is running at all by the time the packet arrives.
        /// </summary>
        /// <remarks>
        /// Rate-limited to 1 call per second. Same caller-authorization note and the same
        /// stale-call-targets-a-new-run limitation as <see cref="RequestStopProcess"/>.
        /// </remarks>
        [NetworkCallable(maxEventsPerSecond: 1)]
        public void RequestCompleteProcess()
        {
            // Same dual-authority guard as RequestStopProcess.
            if ((!IsProcessOwner() && !Networking.IsOwner(gameObject)) || !_isRunning) return;
            ExecuteComplete();
        }

        private void TakeOverAbandonedProcess()
        {
            SetProcessOwner(Networking.LocalPlayer);

            // Call OnOwnerAbandonedProcess before starting the tick loop, consistent with
            // how StartProcess calls OnProcessStarted before scheduling the first tick.
            // Subclasses that set up state in OnOwnerAbandonedProcess need it to run before
            // the first OnProcessUpdate call.
            OnOwnerAbandonedProcess();

            // OnDeserialization does not fire for the player who called RequestSerialization,
            // so we restart the loop manually here. The 0-second delay keeps the behavior
            // consistent with StartProcess: the first tick runs on the next frame.
            if (_useProcessUpdate && !_updateLoopActive)
            {
                _updateLoopActive = true;
                _nextTickDueAtRealTime = Time.realtimeSinceStartup;
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