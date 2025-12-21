using TMPro;
using UnityEngine;
using VRC.SDKBase;

namespace Tsvrc.Example
{
    public class MessengerExample : TsvrcMessenger
    {
        [SerializeField] private TextMeshProUGUI _statusText;
        [SerializeField] private TMP_InputField _lengthInput;

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

            _statusText.text = $"Generating message for {players.Length} players...";

            // Generate and send message in a custom event to avoid blocking
            SendCustomEventDelayedSeconds(nameof(GenerateAndSendMessage), 1);
        }

        public void GenerateAndSendMessage()
        {
            // Get all current players again
            VRCPlayerApi[] players = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
            VRCPlayerApi.GetPlayers(players);

            int[] playerIds = new int[players.Length];
            for (int i = 0; i < players.Length; i++)
            {
                playerIds[i] = players[i].playerId;
            }

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

            _statusText.text = $"Sending {message.Length} characters to {players.Length} players...";

            // Send the message
            SendMessage(message, playerIds);
        }

        protected override void OnReceiveStarted(int totalChunks)
        {
            _statusText.text = $"Delivery started: 0% (0/{totalChunks} chunks)";
        }

        protected override void OnChunkReceived(int chunkIndex, int totalChunks)
        {
            float percentage = (chunkIndex + 1) / (float)totalChunks * 100f;
            _statusText.text = $"Delivery in progress: {percentage:F1}% ({chunkIndex + 1}/{totalChunks} chunks)";
        }

        protected override void OnAllPlayersReceived(string message)
        {
            _statusText.text = $"All players have received the message! Length: {message.Length} characters";
        }

        protected override void OnReceiveFailed(MessageError errorCode)
        {
            switch (errorCode)
            {
                case MessageError.DeliveryAlreadyInProgress:
                    _statusText.text = "Error: A message delivery is already in progress.";
                    break;
                case MessageError.TooManyPlayers:
                    _statusText.text = "Error: Too many players for message delivery.";
                    break;
                case MessageError.OwnerLeftDuringDelivery:
                    _statusText.text = "Error: Owner left during message delivery.";
                    break;
                case MessageError.EmptyMessage:
                    _statusText.text = "Error: Cannot send empty message.";
                    break;
                case MessageError.MessageTooLarge:
                    _statusText.text = "Error: Message is too large to send.";
                    break;
                case MessageError.Cancelled:
                    _statusText.text = "Error: Message delivery was cancelled.";
                    break;
                default:
                    _statusText.text = $"Error: Message delivery failed (code: {errorCode})";
                    break;
            }
        }
    }
}
