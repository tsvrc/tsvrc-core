using Tsvrc.TsNetworking.Utils;
using UdonSharp;
using VRC.SDKBase;

namespace Tsvrc.TsNetworking
{
    /// <summary>
    /// TsvrcMessenger - High-level message transmission for VRChat using network events.
    /// Provides a complete messaging API with network event handling.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class TsvrcMessenger : TsvrcReceiver
    {
        /// <summary>
        /// Sends a message to specified players using network events.
        /// Starts a ready check to track when all players have received it.
        /// </summary>
        /// <param name="message">Message to send</param>
        /// <param name="targetPlayerIds">Array of player IDs to target</param>
        public override void SendMessage(string message, int[] targetPlayerIds)
        {
            base.SendMessage(message, targetPlayerIds);
        }

        /// <summary>
        /// Sends a message to all players in the instance.
        /// </summary>
        /// <param name="message">Message to send</param>
        public void SendMessageToAll(string message)
        {
            VRCPlayerApi[] players = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
            VRCPlayerApi.GetPlayers(players);

            int[] playerIds = new int[players.Length];
            for (int i = 0; i < players.Length; i++)
            {
                playerIds[i] = players[i].playerId;
            }

            base.SendMessage(message, playerIds);
        }

        /// <summary>
        /// Cancels the current message transmission.
        /// </summary>
        public void CancelMessage()
        {
            CancelSend();
        }

        /// <summary>
        /// Called when message transmission starts (for targeted players only).
        /// </summary>
        /// <param name="totalChunks">Total number of chunks to receive</param>
        protected override void OnReceiveStarted(int totalChunks) { }

        /// <summary>
        /// Called when a chunk is received (for targeted players only).
        /// </summary>
        /// <param name="chunkIndex">Index of the received chunk</param>
        /// <param name="totalChunks">Total number of chunks</param>
        protected override void OnChunkReceived(int chunkIndex, int totalChunks) { }

        /// <summary>
        /// Called when all players have received the complete message (for targeted players only).
        /// Override this to handle received messages.
        /// </summary>
        /// <param name="message">The complete received message</param>
        protected override void OnAllPlayersReceived(string message) { }

        /// <summary>
        /// Called when message transmission fails (for targeted players only).
        /// </summary>
        /// <param name="errorCode">The error that occurred</param>
        protected override void OnReceiveFailed(MessageError errorCode) { }

        /// <summary>
        /// Called when message transmission is cancelled (for targeted players only).
        /// </summary>
        protected override void OnReceiveCancelled() { }
    }
}
