using TMPro;
using Tsvrc.TsNetworking;
using Tsvrc.TsNetworking.Utils;
using UnityEngine;

namespace Tsvrc.Example
{
    public class PlayerReadyExample : TsvrcPlayerReadyChecker
    {
        [SerializeField] private TextMeshProUGUI _statusText;

        public override void Interact()
        {
            if (IsProcessRunning()) return;

            string[] playerIds = TsPlayerUtils.GetAllPlayerIDs();

            _statusText.text = $"Starting ready check for: {string.Join(", ", playerIds)}";

            StartReadyCheck(playerIds);
        }

        protected override void OnReadyCheckStarted(string[] playerIds)
        {
            _statusText.text = $"Ready check started for {playerIds.Length} players.";

            SendCustomEventDelayedSeconds(nameof(SetReadyEvent), 2f);
        }

        public void SetReadyEvent()
        {
            SetReady();
        }

        protected override void OnReadyCheckCompleted(string[] playerIds)
        {
            _statusText.text = $"Ready check completed successfully! {playerIds.Length} players are ready.";
        }

        protected override void OnReadyCheckCancelled()
        {
            _statusText.text = "Ready check was cancelled.";
        }
    }
}