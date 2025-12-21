using TMPro;
using Tsvrc.TsNetworking;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace Tsvrc.Example
{
    public class PlayerReadyExample : TsvrcPlayerReady
    {
        [SerializeField] private TextMeshProUGUI _statusText;

        public override void Interact()
        {
            // Get all current players in the instance
            VRCPlayerApi[] players = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
            VRCPlayerApi.GetPlayers(players);

            // Extract their player IDs
            int[] playerIds = new int[players.Length];
            for (int i = 0; i < players.Length; i++)
            {
                playerIds[i] = players[i].playerId;
            }

            _statusText.text = "Starting ready check...";

            StartReadyCheck(playerIds);
            SendCustomNetworkEvent(NetworkEventTarget.All, "SimulateReady");
        }

        public void SimulateReady()
        {
            SendCustomEventDelayedSeconds("CustomSetReady", 2f);
        }

        public void CustomSetReady()
        {
            SetReady();
        }

        protected override void OnCheckStarted(int[] expectedPlayerIds)
        {
            int playerId = Networking.LocalPlayer.playerId;
            foreach (int id in expectedPlayerIds)
            {
                if (id == playerId)
                {
                    _statusText.text = "Ready check started. Please set ready.";
                    return;
                }
            }
        }

        protected override void OnAllPlayersReady(int[] playerIds)
        {
            _statusText.text = $"All {playerIds.Length} players are ready!";
        }

        protected override void OnCheckFailed(ReadyCheckError errorCode)
        {
            switch (errorCode)
            {
                case ReadyCheckError.CheckAlreadyInProgress:
                    _statusText.text = "Error: A ready check is already in progress.";
                    break;
                case ReadyCheckError.ExceededMaxPlayers:
                    _statusText.text = "Error: Too many players for ready check.";
                    break;
                case ReadyCheckError.OwnerLeftDuringCheck:
                    _statusText.text = "Error: Owner left during ready check.";
                    break;
                default:
                    _statusText.text = $"Error: Ready check failed (code: {errorCode})";
                    break;
            }
        }
    }
}