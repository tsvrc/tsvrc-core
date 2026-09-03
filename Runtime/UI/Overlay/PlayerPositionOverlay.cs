using Tsvrc.Core;
using Tsvrc.Player;
using Tsvrc.Tracking;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Tsvrc.UI
{
    /// <summary>
    /// Tracks a set of players and reports their world position on a periodic show/hide
    /// ("blink") cycle. Pure backend - reports each visible player's <see cref="Vector3"/>
    /// position and lets <see cref="Renderer"/> decide how it's presented. See
    /// <see cref="PlayerMarkerRenderer"/>. Every client runs its own local tick loop; nothing
    /// about rendering is synced.
    ///
    /// Two independent ticks: the remote tick (<see cref="RemoteUpdateInterval"/>) caches
    /// tracked players' world positions; the blink tick (<see cref="MarkerOnDuration"/>/
    /// <see cref="MarkerOffDuration"/>) alternates showing that cache via
    /// <see cref="PlayerMarkerRenderer.OnMarkerVisible"/> and hiding, always finishing with one
    /// <see cref="PlayerMarkerRenderer.OnPresent"/> call.
    ///
    /// Usage: assign <see cref="Renderer"/>, call <see cref="StartOverlay"/> to begin, use
    /// <see cref="PlayerTracker.AddTrackedPlayers"/>/<see cref="PlayerTracker.RemoveTrackedPlayers"/>
    /// to change the set, call <see cref="StopOverlay"/> to end. Subscribe to
    /// <see cref="OnOverlayUpdatedEvent"/> for a callback after each cycle.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    [TsWorldExtensionPoint("TsPlayerPositionOverlay")]
    public class PlayerPositionOverlay : PlayerTracker
    {
        /// <summary>
        /// Emitted via <see cref="TsvrcBehaviour.TsEmit"/> after every cycle completes, including
        /// when the overlay is cleared.
        /// </summary>
        public const string OnOverlayUpdatedEvent = "OnOverlayUpdated";

        [Header("Marker Rendering")]
        [Tooltip("Decides what each tracked player's marker looks like and how it's presented. See PlayerMarkerRenderer.")]
        public PlayerMarkerRenderer Renderer;

        [Header("Performance")]
        [Tooltip("How often remote player markers refresh, in seconds.")]
        [Min(0.05f)] public float RemoteUpdateInterval = 0.3f;

        [Header("Marker Timing")]
        [Tooltip("How long player markers stay visible before the overlay clears, in seconds.")]
        [Min(0.1f)] public float MarkerOnDuration = 2f;

        [Tooltip("How long the overlay stays blank before markers reappear, in seconds.")]
        [Min(0.1f)] public float MarkerOffDuration = 1f;

        // Pre-allocated, reused every tick to avoid GC pressure.
        private VRCPlayerApi[] _playerBuffer;      // tracked players, from _ResolveTrackedPlayers
        private VRCPlayerApi[] _allPlayersBuffer;  // all instance players, input scratch space

        private bool _markersVisible;

        // Populated by the remote tick, consumed by the blink tick - lets it present without
        // its own VRCPlayerApi.GetPlayers() call.
        private Vector3[] _cachedWorldPositions;
        private float[] _cachedHeadings;   // only meaningful when Renderer.UsesHeading
        private bool[] _cachedIsLocalArr;
        private string[] _cachedPlayerIdArr;
        private int _cachedPlayerCount;

        private bool _isOverlayUpdating;

        // VRChat's per-instance player cap. Sizes every buffer above.
        private const int MaxTrackedPlayers = 82;

        // Each schedule call increments its counter; each fired event decrements it. A counter
        // still above 0 when a tick fires means a newer tick is already queued, so this one is
        // stale and discarded.
        private int _scheduledBlinkTickCount;
        private int _scheduledRemoteTickCount;

        /// <summary>Starts the overlay, tracking the given player IDs.</summary>
        public void StartOverlay(string[] playerIds)
        {
            if (Renderer == null)
            {
                LogError("Renderer is not assigned.");
                return;
            }

            _EnsureBuffers();
            StartPlayerTracking(playerIds);
        }

        // Lazily allocates the per-slot caches. Idempotent, called from both StartOverlay and
        // OnTrackingStarted so tracking started via the inherited StartPlayerTracking directly
        // still works.
        private void _EnsureBuffers()
        {
            if (_cachedWorldPositions != null) return;
            _playerBuffer = new VRCPlayerApi[MaxTrackedPlayers];
            _allPlayersBuffer = new VRCPlayerApi[MaxTrackedPlayers];
            _cachedWorldPositions = new Vector3[MaxTrackedPlayers];
            _cachedHeadings = new float[MaxTrackedPlayers];
            _cachedIsLocalArr = new bool[MaxTrackedPlayers];
            _cachedPlayerIdArr = new string[MaxTrackedPlayers];
        }

        /// <summary>Stops the overlay and clears it on all clients.</summary>
        public void StopOverlay()
        {
            StopPlayerTracking();
        }

        protected override void OnTrackingStarted(string[] playerIds)
        {
            if (Renderer == null) return;
            _EnsureBuffers();
            _StartOverlay();
        }

        protected override void OnTrackingDeserialization()
        {
            if (!IsProcessRunning() || _isOverlayUpdating || Renderer == null) return;
            _EnsureBuffers();
            _StartOverlay();
        }

        protected override void OnTrackingStopped(string[] playerIds)
        {
            _StopOverlay();
            _ClearAndFlush();
        }

        protected override void OnTrackingCompleted(string[] playerIds)
        {
            _StopOverlay();
            _ClearAndFlush();
        }

        /// <summary>
        /// Blink tick. Alternates presenting all tracked markers and presenting none. Remote
        /// positions come from the cache; the local player is refreshed fresh. Always calls
        /// <see cref="PlayerMarkerRenderer.OnPresent"/> once. Do not call directly.
        /// </summary>
        public void _OnBlinkTick()
        {
            _scheduledBlinkTickCount--;
            if (_scheduledBlinkTickCount > 0 || !_isOverlayUpdating) return;

            _markersVisible = !_markersVisible;

            if (_markersVisible)
            {
                // Remote players come from the cache; local player is presented fresh below.
                bool localPlayerTracked = false;
                for (int i = 0; i < _cachedPlayerCount; i++)
                {
                    if (_cachedIsLocalArr[i])
                    {
                        localPlayerTracked = true;
                        continue;
                    }
                    Renderer.OnMarkerVisible(_cachedWorldPositions[i], false, _cachedHeadings[i], _cachedPlayerIdArr[i]);
                }

                if (localPlayerTracked)
                {
                    VRCPlayerApi localPlayer = Networking.LocalPlayer;
                    if (localPlayer != null && localPlayer.IsValid())
                        _PresentPlayer(localPlayer);
                }
            }

            Renderer.OnPresent();
            TsEmit(OnOverlayUpdatedEvent);

            _ScheduleNextBlinkTick();
        }

        /// <summary>
        /// Remote tick. Resolves all tracked players and caches their position/heading for the
        /// blink tick. Never calls the renderer. Fires every
        /// <see cref="RemoteUpdateInterval"/> seconds. Do not call directly.
        /// </summary>
        public void _OnRemoteTick()
        {
            _scheduledRemoteTickCount--;
            if (_scheduledRemoteTickCount > 0 || !_isOverlayUpdating) return;

            int count = _ResolveTrackedPlayers();

            // Compacted to valid players only, so the blink tick needs no null checks.
            int cacheCount = 0;
            for (int i = 0; i < count; i++)
            {
                VRCPlayerApi player = _playerBuffer[i];
                if (player == null || !player.IsValid()) continue;

                // Skip GetRotation() (quaternion-to-Euler) unless the renderer reads heading.
                float heading = Renderer.UsesHeading
                    ? player.GetRotation().eulerAngles.y : 0f;

                _cachedWorldPositions[cacheCount] = player.GetPosition();
                _cachedHeadings[cacheCount] = heading;
                _cachedIsLocalArr[cacheCount] = player.isLocal;
                _cachedPlayerIdArr[cacheCount] = TsPlayer.GetPlayerID(player);
                cacheCount++;
            }
            _cachedPlayerCount = cacheCount;

            _ScheduleNextRemoteTick();
        }

        // Presents a player at their current world position - the local-player fresh path.
        private void _PresentPlayer(VRCPlayerApi player)
        {
            float heading = Renderer.UsesHeading
                ? player.GetRotation().eulerAngles.y : 0f;
            Renderer.OnMarkerVisible(player.GetPosition(), true, heading, TsPlayer.GetPlayerID(player));
        }

        private void _StartOverlay()
        {
            if (_isOverlayUpdating) return;
            _isOverlayUpdating = true;
            _cachedPlayerCount = 0;
            _markersVisible = false;
            // Delay the first blink so the remote tick populates the cache first.
            _scheduledBlinkTickCount++;
            SendCustomEventDelayedSeconds(nameof(_OnBlinkTick), RemoteUpdateInterval + 0.05f);
            _ScheduleNextRemoteTick();
        }

        private void _StopOverlay()
        {
            _isOverlayUpdating = false;
        }

        private void _ScheduleNextBlinkTick()
        {
            _scheduledBlinkTickCount++;
            // After a show, schedule the hide; after a hide, schedule the show.
            float delay = _markersVisible ? MarkerOnDuration : MarkerOffDuration;
            SendCustomEventDelayedSeconds(nameof(_OnBlinkTick), delay);
        }

        private void _ScheduleNextRemoteTick()
        {
            _scheduledRemoteTickCount++;
            SendCustomEventDelayedSeconds(nameof(_OnRemoteTick), RemoteUpdateInterval);
        }

        /// <summary>
        /// Resolves tracked player IDs to live <see cref="VRCPlayerApi"/> references into
        /// <see cref="_playerBuffer"/>. Returns the count found. Zero-alloc: matches each id's
        /// parsed numeric suffix against <c>VRCPlayerApi.playerId</c> directly.
        /// </summary>
        private int _ResolveTrackedPlayers()
        {
            string[] ids = LastPlayerIds;
            if (ids.Length == 0) return 0;

            // VRCPlayerApi.GetPlayers() writes in-place and pads with null beyond player count.
            int totalCount = VRCPlayerApi.GetPlayerCount();
            VRCPlayerApi.GetPlayers(_allPlayersBuffer);

            int count = 0;
            for (int i = 0; i < ids.Length && count < _playerBuffer.Length; i++)
            {
                int targetIntId = _ParsePlayerIntId(ids[i]);
                if (targetIntId < 0) continue; // malformed ID, skip
                for (int j = 0; j < totalCount; j++)
                {
                    VRCPlayerApi p = _allPlayersBuffer[j];
                    if (p == null || !p.IsValid()) continue;
                    if (p.playerId == targetIntId)
                    {
                        _playerBuffer[count++] = p;
                        break;
                    }
                }
            }
            return count;
        }

        // Parses the trailing int after the last '#'. Returns -1 if malformed or missing.
        private int _ParsePlayerIntId(string id)
        {
            int hashPos = -1;
            for (int i = id.Length - 1; i >= 0; i--)
            {
                if (id[i] == '#') { hashPos = i; break; }
            }
            if (hashPos < 0 || hashPos == id.Length - 1) return -1;
            int result = 0;
            for (int i = hashPos + 1; i < id.Length; i++)
            {
                char c = id[i];
                if (c < '0' || c > '9') return -1;
                result = result * 10 + (c - '0');
            }
            return result;
        }

        private void _ClearAndFlush()
        {
            if (Renderer == null) return;
            // Same path a blink-tick hide takes, reused for the round-end/stop case.
            Renderer.OnPresent();
            TsEmit(OnOverlayUpdatedEvent);
        }

    }
}
