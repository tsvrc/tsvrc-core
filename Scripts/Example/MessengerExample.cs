using TMPro;
using Tsvrc.Player;
using Tsvrc.TsNetworking;
using Tsvrc.TsNetworking.Utils;
using UnityEngine;

namespace Tsvrc.Example
{
    public class MessengerExample : TsvrcDataTransferer
    {
        [SerializeField] private TextMeshProUGUI _statusText;
        [SerializeField] private TMP_InputField _lengthInput;

        #region Unity Methods

        public override void Interact()
        {
            string[] playerIds = TsPlayer.GetAllPlayerIDs();

            _statusText.text = $"Generating message for {playerIds.Length} players...";

            GenerateAndSendMessage();
        }

        #endregion

        #region Public Methods

        public void GenerateAndSendMessage()
        {
            string[] playerIds = TsPlayer.GetAllPlayerIDs();

            // Parse length from input field, default to 1000 if invalid
            int messageLength = 1000;
            if (_lengthInput != null && !string.IsNullOrEmpty(_lengthInput.text))
            {
                if (int.TryParse(_lengthInput.text, out int parsedLength))
                {
                    messageLength = parsedLength;
                }
            }

            // Clamp to reasonable values
            messageLength = Mathf.Clamp(messageLength, 1, 10000000);

            // Generate random characters
            System.Text.StringBuilder sb = new System.Text.StringBuilder(messageLength);
            System.Random random = new System.Random();
            for (int i = 0; i < messageLength; i++)
            {
                // Generate random printable ASCII character (33-126)
                char randomChar = (char)random.Next(33, 127);
                sb.Append(randomChar);
            }
            string message = sb.ToString();

            _statusText.text = $"Sending {message.Length} characters to {playerIds.Length} players...";

            TransferData(message, playerIds);
        }

        #endregion

        #region TsvrcDataReceiver Callbacks

        protected override void OnDataChunkReceivedAsTrackedPlayer(int chunkIndex, int totalChunks)
        {
            base.OnDataChunkReceivedAsTrackedPlayer(chunkIndex, totalChunks);

            float percentage = chunkIndex / (float)totalChunks * 100f;
            _statusText.text = $"Delivery in progress: {percentage:F1}% ({chunkIndex}/{totalChunks} chunks)";
        }

        #endregion

        #region TsvrcDataTransferer Callbacks

        protected override void OnDataTransfererStartedAsTrackedPlayer(string[] playerIds)
        {
            base.OnDataTransfererStartedAsTrackedPlayer(playerIds);

            _statusText.text = "Data transfer started...";
        }

        protected override void OnDataTransfererStoppedAsTrackedPlayer(string[] playerIds)
        {
            base.OnDataTransfererStoppedAsTrackedPlayer(playerIds);

            _statusText.text = "Data transfer stopped.";
        }

        protected override void OnDataTransfererCompletedAsTrackedPlayer(string data, string[] playerIds)
        {
            base.OnDataTransfererCompletedAsTrackedPlayer(data, playerIds);

            _statusText.text = $"Data transfer completed! Received {data.Length} characters from all players.";
        }

        #endregion
    }
}
