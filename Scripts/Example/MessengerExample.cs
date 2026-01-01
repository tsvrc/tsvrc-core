using TMPro;
using Tsvrc.TsNetworking;
using Tsvrc.TsNetworking.Utils;
using UnityEngine;

namespace Tsvrc.Example
{
    public class MessengerExample : TsvrcDataTransferer
    {
        [SerializeField] private TextMeshProUGUI _statusText;
        [SerializeField] private TMP_InputField _lengthInput;

        public override void Interact()
        {
            string[] playerIds = TsPlayerUtils.GetAllPlayerIDs();

            _statusText.text = $"Generating message for {playerIds.Length} players...";

            GenerateAndSendMessage();
        }

        public void GenerateAndSendMessage()
        {
            string[] playerIds = TsPlayerUtils.GetAllPlayerIDs();

            // Parse length from input field, default to 500000 if invalid
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

        protected override void OnDataReceptionStarted()
        {
            _statusText.text = "Data reception started...";
        }

        protected override void OnDataChunkReceived(int chunkIndex, int totalChunks)
        {
            float percentage = chunkIndex / (float)totalChunks * 100f;
            _statusText.text = $"Delivery in progress: {percentage:F1}% ({chunkIndex}/{totalChunks} chunks)";
        }

        protected override void OnDataReceptionCompleted(string data)
        {
            _statusText.text = $"All players have received the message! Length: {data.Length} characters";
        }
    }
}
