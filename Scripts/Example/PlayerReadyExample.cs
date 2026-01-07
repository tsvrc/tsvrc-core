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

        #region Public Methods

        public override void Interact()
        {
            if (IsProcessRunning()) return;

            string[] playerIds = TsPlayerUtils.GetAllPlayerIDs();

            _statusText.text = $"Starting ready check for: {string.Join(", ", playerIds)}";

            StartReadyCheck(playerIds);
        }

        public void SetReadyEvent()
        {
            SetReady();
        }

        #endregion

        #region TsvrcPlayerReadyChecker Callbacks

        protected override void OnReadyCheckStartedAsTrackedPlayer(string[] playerIds)
        {
            _statusText.text = $"Ready check started for {playerIds.Length} players.";

            SendCustomEventDelayedSeconds(nameof(SetReadyEvent), 2f);
        }

        protected override void OnReadyCheckCompletedAsTrackedPlayer(string[] playerIds)
        {
            _statusText.text = $"Ready check completed successfully! {playerIds.Length} players are ready.";
        }

        protected override void OnReadyCheckStoppedAsTrackedPlayer(string[] playerIds)
        {
            _statusText.text = "Ready check was stopped.";
        }

        #endregion
    }
}