using UdonSharp;

namespace Tsvrc.TsNetworking.Utils
{

    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class TsvrcReceiver : TsvrcSender
    {
        // Receiver state (targeted clients only)
        protected string[] _receivedChunks;

        protected override void OnSendStarted(int totalChunks)
        {
            if (IsLocalPlayerTargeted())
            {
                _receivedChunks = new string[totalChunks];
                OnReceiveStarted(totalChunks);
            }
        }

        protected virtual void OnReceiveStarted(int totalChunks) { }

        protected override void OnSendedToAllPlayers()
        {
            if (IsLocalPlayerTargeted())
            {
                string message = ReassembleMessage(_receivedChunks.Length);
                ResetReceiverState();
                OnAllPlayersReceived(message);
            }
        }

        protected override void OnChunkSended(int chunkIndex, int totalChunks, string chunkData)
        {
            if (IsLocalPlayerTargeted())
            {
                _receivedChunks[chunkIndex] = chunkData;
                OnChunkReceived(chunkIndex, totalChunks);
            }
        }

        protected virtual void OnChunkReceived(int chunkIndex, int totalChunks) { }

        protected virtual void OnAllPlayersReceived(string message) { }

        protected override void OnSendFailed(MessageError errorCode)
        {
            OnReceiveFailed(errorCode);
        }

        protected virtual void OnReceiveFailed(MessageError errorCode) { }

        protected override void OnSendCancelled()
        {
            ResetReceiverState();
            OnReceiveCancelled();
        }

        protected virtual void OnReceiveCancelled() { }

        protected string ReassembleMessage(int totalChunks)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < totalChunks; i++)
            {
                sb.Append(_receivedChunks[i]);
            }
            return sb.ToString();
        }

        protected void ResetReceiverState()
        {
            _receivedChunks = null;
        }
    }
}
