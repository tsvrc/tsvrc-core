using Tsvrc.Core;
using UdonSharp;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace Tsvrc.Timing
{
    /// <summary>
    /// Tracks time between a synced start and end using VRChat's server clock.
    /// All clients share the same time anchors and compute elapsed time locally.
    /// No per-tick network sync is needed.
    ///
    /// Lifecycle events fire on all clients:
    ///   - Owner path: OnProcessStarted/Stopped/Completed hooks.
    ///   - Non-owner path: running state transition detection in OnDeserialization.
    ///   - Late joiners arriving mid-run receive OnTimerStartedEvent once.
    ///
    /// Remaining time: remainingMs = DurationMs - GetElapsedMilliseconds()
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class TsvrcTimer : TsvrcProcess
    {
        public const string OnTimerStartedEvent = "OnTimerStarted";
        public const string OnTimerStoppedEvent = "OnTimerStopped";
        public const string OnTimerCompletedEvent = "OnTimerCompleted";
        public const string OnTimerPausedEvent = "OnTimerPaused";
        public const string OnTimerResumedEvent = "OnTimerResumed";
        public const string OnTimerUpdatedEvent = "OnTimerUpdated";
        public const string OnTimerDeserializationEvent = "OnTimerDeserialization";

        // Server-time anchors. While running, elapsed is:
        //   offset + (currentServerMs - startServerMs)
        [UdonSynced] private int _startServerTimeMs = 0;
        [UdonSynced] private int _elapsedOffsetMs = 0;
        // Full requested duration in milliseconds. Synced so all clients can compute
        // remaining time via DurationMs - GetElapsedMilliseconds() without extra packets.
        // 0 means open-ended (no auto-complete).
        [UdonSynced] private int _durationMs = 0;
        // Preserved after stop/complete so OnDeserialization on non-owners can emit
        // the correct stopped vs completed event on the running->idle transition.
        // Reset to false in OnProcessStarted at the beginning of each new run.
        [UdonSynced] private bool _wasCompleted = false;
        [UdonSynced] private bool _isPaused = false;

        private const float _localUpdateInterval = 0.25f;
        private bool _localUpdateLoopActive = false;
        private bool _hasObservedState = false;
        private int _lastObservedElapsedMs = 0;
        private bool _lastObservedIsRunning = false;
        private bool _lastObservedIsPaused = false;

        /// <summary>
        /// The last locally evaluated elapsed time in milliseconds.
        /// </summary>
        public int LastElapsedMilliseconds { get; private set; } = 0;

        /// <summary>
        /// The last locally evaluated elapsed time in seconds.
        /// </summary>
        public float LastElapsedSeconds => LastElapsedMilliseconds * 0.001f;

        /// <summary>
        /// The server time in milliseconds at which the timer was started.
        /// </summary>
        public int StartServerTimeMs => _startServerTimeMs;

        /// <summary>
        /// The requested duration in milliseconds. 0 means open-ended.
        /// Use this to derive remaining time: remainingMs = DurationMs - GetElapsedMilliseconds()
        /// </summary>
        public int DurationMs => _durationMs;

        public override void OnDeserialization()
        {
            base.OnDeserialization();

            bool isRunning = IsProcessRunning();
            bool isPaused = _isPaused;

            // Local update loop runs only when the timer is actively counting.
            // Pausing stops the loop; deserialization of a resume will restart it.
            if (isRunning && !isPaused) StartLocalUpdateLoop();
            else StopLocalUpdateLoop();

            // Capture observation state before UpdateElapsedSnapshot overwrites it.
            bool wasRunning = _lastObservedIsRunning;
            bool wasPaused = _lastObservedIsPaused;
            bool hadObserved = _hasObservedState;

            UpdateElapsedSnapshot();

            // Fire lifecycle events on non-owner clients by detecting running state changes.
            // OnDeserialization never fires on the sender of RequestSerialization (VRChat guarantee),
            // so the owner never gets duplicate events since it receives them directly from Execute*.
            if (!wasRunning && isRunning)
            {
                // Fires for late joiners arriving mid-run (possibly paused) and normal start.
                OnTimerStarted();
                TsEmit(OnTimerStartedEvent);
                // Late joiners arriving while paused only see the running state go from false
                // to true. The pause/resume branch below is skipped because hadObserved is false.
                // Also fire OnTimerPaused so the consumer starts in the correct paused state
                // without needing to check _isPaused manually.
                if (isPaused)
                {
                    OnTimerPaused();
                    TsEmit(OnTimerPausedEvent);
                }
            }
            else if (hadObserved && wasRunning && !isRunning)
            {
                // hadObserved guards against false positives for late joiners arriving after stop.
                if (_wasCompleted)
                {
                    OnTimerCompleted();
                    TsEmit(OnTimerCompletedEvent);
                }
                else
                {
                    OnTimerStopped();
                    TsEmit(OnTimerStoppedEvent);
                }
            }
            else if (hadObserved && wasRunning && isRunning)
            {
                // Detect pause/resume within a running timer.
                if (!wasPaused && isPaused)
                {
                    OnTimerPaused();
                    TsEmit(OnTimerPausedEvent);
                }
                else if (wasPaused && !isPaused)
                {
                    OnTimerResumed();
                    TsEmit(OnTimerResumedEvent);
                }
            }

            OnTimerDeserialization();
            TsEmit(OnTimerDeserializationEvent);
        }

        protected override void OnProcessStarted()
        {
            _startServerTimeMs = GetServerTimeMilliseconds();
            _elapsedOffsetMs = 0;
            _wasCompleted = false;
            _isPaused = false;
            LastElapsedMilliseconds = 0;

            StartLocalUpdateLoop();
            UpdateElapsedSnapshot();

            // base.StartProcess serializes before this callback executes, so serialize again
            // to propagate all newly written anchors to other clients.
            RequestSerialization();

            OnTimerStarted();
            TsEmit(OnTimerStartedEvent);
            // OnTimerUpdatedEvent already emitted inside UpdateElapsedSnapshot above.
        }

        protected override void OnProcessStopped()
        {
            // TsvrcProcess.ExecuteStop sets _isRunning to false before calling this hook, so
            // GetElapsedMilliseconds() would return _elapsedOffsetMs early and miss all elapsed
            // time since the last start. ComputeRawElapsedMs() bypasses that guard.
            // When stopped while paused, _elapsedOffsetMs is already the correct frozen value
            // set by ExecutePause. Calling ComputeRawElapsedMs() would add extra wall time on
            // top of it and overcount, so we skip it in that case.
            if (!_isPaused)
                _elapsedOffsetMs = ComputeRawElapsedMs();
            StopLocalUpdateLoop();
            UpdateElapsedSnapshot();

            OnTimerStopped();
            TsEmit(OnTimerStoppedEvent);
            // OnTimerUpdatedEvent already emitted inside UpdateElapsedSnapshot above.
        }

        protected override void OnProcessCompleted()
        {
            // Same as OnProcessStopped: _isRunning is already false when this hook fires.
            // If completed while paused, _elapsedOffsetMs is already the correct frozen value.
            if (!_isPaused)
                _elapsedOffsetMs = ComputeRawElapsedMs();
            StopLocalUpdateLoop();
            UpdateElapsedSnapshot();

            OnTimerCompleted();
            TsEmit(OnTimerCompletedEvent);
            // OnTimerUpdatedEvent already emitted inside UpdateElapsedSnapshot above.
        }

        protected override void OnProcessUpdate()
        {
            // Auto-complete when elapsed reaches the requested duration.
            // GetElapsedMilliseconds() returns the frozen offset when paused, so this
            // naturally does nothing while paused without needing an explicit guard.
            if (_durationMs == 0) return;
            if (GetElapsedMilliseconds() < _durationMs) return;

            CompleteProcess();
        }

        protected override void OnProcessCleanup(bool isCompleted)
        {
            base.OnProcessCleanup(isCompleted);

            if (IsProcessRunning()) return;

            // Record completion status so OnDeserialization on non-owner clients can
            // distinguish a natural completion from an early stop on the running->idle transition.
            // Reset to false in OnProcessStarted at the beginning of each new run.
            _wasCompleted = isCompleted;
            _isPaused = false;
            _durationMs = 0;

            // _startServerTimeMs and _elapsedOffsetMs are intentionally preserved here.
            // They hold the final run's anchors and are serialized by InternalCleanup's
            // RequestSerialization so non-owners can display the correct final elapsed time.
            // They are reset at the beginning of the next run in OnProcessStarted.

            StopLocalUpdateLoop();
        }

        /// <summary>
        /// Starts the shared timer with no end time (open-ended, counts up indefinitely).
        /// </summary>
        public void StartTimer()
        {
            // StartProcess() returns early if already running, but only after this method
            // has already written _durationMs. That would silently corrupt the running timer
            // with no serialization to fix it, so we guard here instead.
            if (IsProcessRunning()) return;
            _durationMs = 0;
            StartProcess();
        }

        /// <summary>
        /// Starts the shared timer and auto-completes after <paramref name="durationMilliseconds"/>.
        /// Pausing extends the effective run time so the full duration is always observed.
        /// </summary>
        /// <param name="durationMilliseconds">How long the timer should run in milliseconds. Negative values are treated as 0 (open-ended).</param>
        public void StartTimer(int durationMilliseconds)
        {
            // Same guard as StartTimer(): prevent mutating _durationMs while already running.
            if (IsProcessRunning()) return;
            _durationMs = durationMilliseconds < 0 ? 0 : durationMilliseconds;
            StartProcess(useProcessUpdate: _durationMs > 0);
        }

        /// <summary>
        /// Stops the shared timer before completion. Forwards to the owner if called by a non-owner.
        /// </summary>
        public void StopTimer()
        {
            StopProcess();
        }

        /// <summary>
        /// Pauses the timer, freezing elapsed time. Forwards to the owner if called by a non-owner.
        /// </summary>
        public void PauseTimer()
        {
            if (!IsProcessRunning() || _isPaused) return;

            if (!IsProcessOwner() && !Networking.IsOwner(gameObject))
            {
                SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RequestPauseTimer));
                return;
            }

            ExecutePause();
        }

        /// <summary>
        /// Resumes a paused timer. Forwards to the owner if called by a non-owner.
        /// The elapsed offset is preserved so the timer continues from where it paused.
        /// </summary>
        public void ResumeTimer()
        {
            if (!IsProcessRunning() || !_isPaused) return;

            if (!IsProcessOwner() && !Networking.IsOwner(gameObject))
            {
                SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(RequestResumeTimer));
                return;
            }

            ExecuteResume();
        }

        // Received by the owner when a non-owner calls PauseTimer().
        [NetworkCallable(maxEventsPerSecond: 2)]
        public void RequestPauseTimer()
        {
            if (!IsProcessRunning() || _isPaused) return;
            if (!IsProcessOwner() && !Networking.IsOwner(gameObject)) return;
            ExecutePause();
        }

        // Received by the owner when a non-owner calls ResumeTimer().
        [NetworkCallable(maxEventsPerSecond: 2)]
        public void RequestResumeTimer()
        {
            if (!IsProcessRunning() || !_isPaused) return;
            if (!IsProcessOwner() && !Networking.IsOwner(gameObject)) return;
            ExecuteResume();
        }

        private void ExecutePause()
        {
            // Capture elapsed before freezing. _isRunning is still true here so
            // ComputeRawElapsedMs gives the correct running total.
            _elapsedOffsetMs = ComputeRawElapsedMs();
            _isPaused = true;
            StopLocalUpdateLoop();
            RequestSerialization();
            UpdateElapsedSnapshot();
            OnTimerPaused();
            TsEmit(OnTimerPausedEvent);
        }

        private void ExecuteResume()
        {
            // Move the start anchor to now so elapsed continues from the frozen _elapsedOffsetMs.
            // OnProcessUpdate checks GetElapsedMilliseconds() >= _durationMs using this new anchor,
            // so the remaining duration is preserved automatically.
            _startServerTimeMs = GetServerTimeMilliseconds();
            _isPaused = false;
            StartLocalUpdateLoop();
            RequestSerialization();
            UpdateElapsedSnapshot();
            OnTimerResumed();
            TsEmit(OnTimerResumedEvent);
        }

        /// <summary>
        /// Returns elapsed milliseconds calculated from synced server-time anchor state.
        /// </summary>
        public int GetElapsedMilliseconds()
        {
            // Return frozen offset when not actively counting (stopped or paused).
            if (!IsProcessRunning() || _isPaused) return _elapsedOffsetMs;

            int nowMs = GetServerTimeMilliseconds();
            int elapsedSinceStartMs = nowMs - _startServerTimeMs;
            if (elapsedSinceStartMs < 0) elapsedSinceStartMs = 0;

            int elapsedMs = _elapsedOffsetMs + elapsedSinceStartMs;
            return elapsedMs < 0 ? 0 : elapsedMs;
        }

        /// <summary>
        /// Returns elapsed seconds calculated from synced server-time anchor state.
        /// </summary>
        public float GetElapsedSeconds()
        {
            return GetElapsedMilliseconds() * 0.001f;
        }

        /// <summary>
        /// Returns remaining milliseconds until the timer completes.
        /// Returns 0 when the timer is open-ended (no duration set), already completed, or the elapsed time exceeds the duration.
        /// </summary>
        public int GetRemainingMilliseconds()
        {
            if (_durationMs <= 0) return 0;
            int remaining = _durationMs - GetElapsedMilliseconds();
            return remaining < 0 ? 0 : remaining;
        }

        /// <summary>
        /// Returns remaining seconds until the timer completes.
        /// Returns 0 when the timer is open-ended, already completed, or the elapsed time exceeds the duration.
        /// </summary>
        public float GetRemainingSeconds()
        {
            return GetRemainingMilliseconds() * 0.001f;
        }

        /// <summary>Called on each local client when the timer starts.</summary>
        protected virtual void OnTimerStarted() { }
        /// <summary>Called on each local client when the timer stops before completion.</summary>
        protected virtual void OnTimerStopped() { }
        /// <summary>Called on each local client when the timer completes (duration reached or CompleteProcess called).</summary>
        protected virtual void OnTimerCompleted() { }
        /// <summary>Called on each local client when the timer is paused.</summary>
        protected virtual void OnTimerPaused() { }
        /// <summary>Called on each local client when the timer is resumed.</summary>
        protected virtual void OnTimerResumed() { }
        /// <summary>Called on each local client when synced timer state is deserialized.</summary>
        protected virtual void OnTimerDeserialization() { }

        /// <summary>
        /// Returns the current VRChat server time in milliseconds.
        /// Use this instead of calling Networking.GetServerTimeInMilliseconds() directly.
        /// </summary>
        public int GetServerTimeMilliseconds()
        {
            return Networking.GetServerTimeInMilliseconds();
        }

        // Computes elapsed regardless of IsProcessRunning(). Used in OnProcessStopped and
        // OnProcessCompleted where _isRunning has already been cleared by the base class.
        private int ComputeRawElapsedMs()
        {
            int delta = GetServerTimeMilliseconds() - _startServerTimeMs;
            if (delta < 0) delta = 0;
            int total = _elapsedOffsetMs + delta;
            return total < 0 ? 0 : total;
        }

        // Public because SendCustomEventDelayedSeconds requires a public target method.
        public void _TickLocalElapsed()
        {
            if (!_localUpdateLoopActive) return;

            UpdateElapsedSnapshot();

            // Stop ticking if the process ended or was paused since the last tick.
            if (!IsProcessRunning() || _isPaused)
            {
                _localUpdateLoopActive = false;
                return;
            }

            SendCustomEventDelayedSeconds(nameof(_TickLocalElapsed), _localUpdateInterval);
        }

        private void UpdateElapsedSnapshot()
        {
            bool isRunning = IsProcessRunning();
            bool isPaused = _isPaused;

            LastElapsedMilliseconds = GetElapsedMilliseconds();

            bool stateChanged = LastElapsedMilliseconds != _lastObservedElapsedMs
                || !_hasObservedState
                || isRunning != _lastObservedIsRunning
                || isPaused != _lastObservedIsPaused;

            _hasObservedState = true;
            _lastObservedElapsedMs = LastElapsedMilliseconds;
            _lastObservedIsRunning = isRunning;
            _lastObservedIsPaused = isPaused;

            if (stateChanged)
            {
                TsEmit(OnTimerUpdatedEvent);
            }
        }

        private void StartLocalUpdateLoop()
        {
            if (_localUpdateLoopActive) return;

            _localUpdateLoopActive = true;
            SendCustomEventDelayedSeconds(nameof(_TickLocalElapsed), _localUpdateInterval);
        }

        private void StopLocalUpdateLoop()
        {
            _localUpdateLoopActive = false;
        }
    }
}
