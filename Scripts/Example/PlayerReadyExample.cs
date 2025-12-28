using TMPro;
using Tsvrc.TsNetworking;
using Tsvrc.TsNetworking.Utils;
using UnityEngine;
using VRC.SDKBase;

namespace Tsvrc.Example
{
    public class PlayerReadyExample : TsvrcPlayerReadyChecker
    {
        [SerializeField] private TextMeshProUGUI _statusText;

        public override void Interact()
        {
            // Get all current players in the instance
            VRCPlayerApi[] players = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
            VRCPlayerApi.GetPlayers(players);

            // Convert players to their unique IDs
            string[] playerIds = TsPlayerUtils.ToPlayerIDs(players);

            _statusText.text = "Starting ready check...";

            StartReadyCheck(playerIds);
        }

        protected override void OnReadyCheckStarted(string[] expectedPlayerIds)
        {
            _statusText.text = $"Ready check started for {expectedPlayerIds.Length} players.";

            SendCustomEventDelayedSeconds(nameof(SetReadyEvent), 2f);
        }

        public void SetReadyEvent()
        {
            SetReady();
        }

        protected override void OnAllPlayersReady(string[] playerIds)
        {
            _statusText.text = $"All players are ready! ({playerIds.Length} players)";
        }

        protected override void OnReadyCheckCancelled()
        {
            _statusText.text = "Ready check was cancelled.";
        }
    }
}