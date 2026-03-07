using Tsvrc.Player;
using UdonSharp;
using VRC.SDKBase;

namespace Tsvrc.Network
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class TsAutoPlayerTracker : TsPlayerTracker
    {
        private UdonSharpBehaviour _autoTrackerListener;
        private string _onAutoTrackingStartedEvent = "OnAutoTrackingStarted";
        private string _onAutoTrackingStoppedEvent = "OnAutoTrackingStopped";
        private string _onAutoTrackingCompletedEvent = "OnAutoTrackingCompleted";
        private string _onAutoTrackingDeserializationEvent = "OnAutoTrackingDeserialization";
        private string _onAutoTrackingPlayersAddedEvent = "OnAutoTrackingPlayersAdded";
        private string _onAutoTrackingPlayersRemovedEvent = "OnAutoTrackingPlayersRemoved";

        /// <summary>
        /// Initializes the TsAutoPlayerTracker with a listener and event method names.
        /// On each tracking event, SendCustomEvent is called on the listener using the corresponding name.
        /// Use nameof() for event names to avoid magic strings and get refactor safety.
        /// Read event data from the Last* properties inside the listener's callback methods:
        /// <list type="bullet">
        /// <item><term>onAutoTrackingStartedEvent</term><description>LastPlayerIds</description></item>
        /// <item><term>onAutoTrackingStoppedEvent</term><description>LastPlayerIds</description></item>
        /// <item><term>onAutoTrackingCompletedEvent</term><description>LastPlayerIds</description></item>
        /// <item><term>onAutoTrackingDeserializationEvent</term><description>LastPlayerIds</description></item>
        /// <item><term>onAutoTrackingPlayersAddedEvent</term><description>LastAddedPlayerIds</description></item>
        /// <item><term>onAutoTrackingPlayersRemovedEvent</term><description>LastRemovedPlayerIds</description></item>
        /// </list>
        /// <example>
        /// <code>
        /// autoTracker.TsConstructAutoPlayerTracker(
        ///     this,
        ///     nameof(_OnAutoTrackingStartedMethod),
        ///     nameof(_OnAutoTrackingStoppedMethod),
        ///     nameof(_OnAutoTrackingCompletedMethod),
        ///     nameof(_OnAutoTrackingDeserializationMethod),
        ///     nameof(_OnAutoTrackingPlayersAddedMethod),
        ///     nameof(_OnAutoTrackingPlayersRemovedMethod)
        /// );
        /// </code>
        /// </example>
        /// </summary>
        public void TsConstructAutoPlayerTracker(
            UdonSharpBehaviour listener,
            string onAutoTrackingStartedEvent,
            string onAutoTrackingStoppedEvent,
            string onAutoTrackingCompletedEvent,
            string onAutoTrackingDeserializationEvent,
            string onAutoTrackingPlayersAddedEvent,
            string onAutoTrackingPlayersRemovedEvent
        )
        {
            _autoTrackerListener = listener;
            _onAutoTrackingStartedEvent = onAutoTrackingStartedEvent;
            _onAutoTrackingStoppedEvent = onAutoTrackingStoppedEvent;
            _onAutoTrackingCompletedEvent = onAutoTrackingCompletedEvent;
            _onAutoTrackingDeserializationEvent = onAutoTrackingDeserializationEvent;
            _onAutoTrackingPlayersAddedEvent = onAutoTrackingPlayersAddedEvent;
            _onAutoTrackingPlayersRemovedEvent = onAutoTrackingPlayersRemovedEvent;

            base.TsConstructPlayerTracker(
                this,
                nameof(_OnTrackingStarted),
                nameof(_OnTrackingStopped),
                nameof(_OnTrackingCompleted),
                nameof(_OnTrackingDeserialization),
                nameof(_OnTrackingPlayersAdded),
                nameof(_OnTrackingPlayersRemoved)
            );
        }

        #region TsPlayerTracker Callbacks

        public void _OnTrackingStarted()
        {
            _autoTrackerListener.SendCustomEvent(_onAutoTrackingStartedEvent);
        }

        public void _OnTrackingStopped()
        {
            _autoTrackerListener.SendCustomEvent(_onAutoTrackingStoppedEvent);
        }

        public void _OnTrackingCompleted()
        {
            _autoTrackerListener.SendCustomEvent(_onAutoTrackingCompletedEvent);
        }

        public void _OnTrackingDeserialization()
        {
            _autoTrackerListener.SendCustomEvent(_onAutoTrackingDeserializationEvent);
        }

        public void _OnTrackingPlayersAdded()
        {
            _autoTrackerListener.SendCustomEvent(_onAutoTrackingPlayersAddedEvent);
        }

        public void _OnTrackingPlayersRemoved()
        {
            _autoTrackerListener.SendCustomEvent(_onAutoTrackingPlayersRemovedEvent);
        }

        #endregion

        #region VRChat Callbacks

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (!IsProcessRunning() || !IsProcessOwner()) return;

            var playerId = TsPlayer.GetPlayerID(player);
            var players = TsPlayer.ToArray(playerId);
            AddTrackedPlayers(players);
        }

        #endregion
    }
}