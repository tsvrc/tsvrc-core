using Tsvrc.List.Utils;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace Tsvrc.TsNetworking.Utils
{
    /// <summary>
    /// Error codes for message delivery failures.
    /// </summary>
    public enum MessageError
    {
        DeliveryAlreadyInProgress = 1,
        TooManyPlayers = 2,
        OwnerLeftDuringDelivery = 3,
        EmptyMessage = 4,
        MessageTooLarge = 6,
        Cancelled = 7
    }

    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class TsvrcSender : TsvrcPlayerReadyChecker
    {
        // Chunking constants
        protected const int CHUNK_SIZE = 15000;
        protected const int MAX_MESSAGE_SIZE = 500000;

        // Sender state (owner only)
        protected string[] _messageChunks;
        protected int _currentChunkIndex;
        protected int _totalChunks;

        // Temporary storage for chunking (owner only)
        protected string _pendingMessage;
        protected string[] _pendingTargetPlayerIds;

        // Target tracking (all clients)
        protected string[] _targetPlayerIds = new string[0];

        public virtual void SendTsMessage(string message, string[] targetPlayerIds)
        {
            // Check if a message send is already in progress BEFORE taking ownership
            if (_isCheckInProgress || _messageChunks != null || _pendingMessage != null)
            {
                Debug.LogWarning("[TsvrcSender] A message delivery is already in progress. Cannot start a new send.");
                OnSendFailed(MessageError.DeliveryAlreadyInProgress);
                return;
            }

            if (!IsMessengerOwner())
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }

            if (!ValidateMessage(message))
                return;

            _pendingMessage = message;
            _pendingTargetPlayerIds = targetPlayerIds;

            SendCustomEventDelayedSeconds(nameof(ChunkAndSendMessage), 0.01f);
        }

        private bool IsMessengerOwner()
        {
            return Networking.IsOwner(gameObject);
        }

        protected bool ValidateMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                Debug.LogWarning("[TsvrcSender] Cannot send empty message");
                OnSendFailed(MessageError.EmptyMessage);
                return false;
            }

            if (message.Length > MAX_MESSAGE_SIZE)
            {
                Debug.LogError($"[TsvrcSender] Message too large: {message.Length} chars (max {MAX_MESSAGE_SIZE})");
                OnSendFailed(MessageError.MessageTooLarge);
                return false;
            }

            return true;
        }

        public void ChunkAndSendMessage()
        {
            CreateMessageChunks();
            _currentChunkIndex = 0;

            StartReadyCheck(_pendingTargetPlayerIds);
            ClearPendingData();
        }

        private void CreateMessageChunks()
        {
            _totalChunks = CalculateTotalChunks(_pendingMessage.Length);
            _messageChunks = new string[_totalChunks];

            for (int i = 0; i < _totalChunks; i++)
            {
                _messageChunks[i] = ExtractChunk(_pendingMessage, i);
            }
        }

        private int CalculateTotalChunks(int messageLength)
        {
            return (messageLength + CHUNK_SIZE - 1) / CHUNK_SIZE;
        }

        private string ExtractChunk(string message, int chunkIndex)
        {
            int startIndex = chunkIndex * CHUNK_SIZE;
            int length = System.Math.Min(CHUNK_SIZE, message.Length - startIndex);
            return message.Substring(startIndex, length);
        }

        private void ClearPendingData()
        {
            _pendingMessage = null;
            _pendingTargetPlayerIds = null;
        }

        protected override void OnReadyCheckStarted(string[] expectedPlayerIds)
        {
            _targetPlayerIds = expectedPlayerIds;

            if (!IsMessengerOwner()) return;

            if (_currentChunkIndex == 0)
            {
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SendStartedEvent), _totalChunks);
            }

            SendCurrentChunk();
        }

        [NetworkCallable]
        public void SendStartedEvent(int totalChunks)
        {
            _totalChunks = totalChunks;
            OnSendStarted(_totalChunks);
        }

        private void SendCurrentChunk()
        {
            if (_messageChunks != null && _currentChunkIndex < _totalChunks)
            {
                string chunk = _messageChunks[_currentChunkIndex];
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SendChunkEvent), _currentChunkIndex, _totalChunks, chunk);
            }
        }

        [NetworkCallable]
        public void SendChunkEvent(int chunkIndex, int totalChunks, string chunkData)
        {
            OnChunkSended(chunkIndex, totalChunks, chunkData);
            SetReady();
        }

        protected virtual void OnChunkSended(int chunkIndex, int totalChunks, string chunkData) { }

        protected override void OnAllPlayersReady(string[] playerIds)
        {
            if (!IsMessengerOwner()) return;

            if (_messageChunks != null && _currentChunkIndex < _totalChunks - 1)
            {
                _currentChunkIndex++;
                StartReadyCheck(playerIds);
            }
            else
            {
                ClearSenderState();
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SendedToAllPlayersEvent));
            }
        }

        [NetworkCallable]
        public void SendedToAllPlayersEvent()
        {
            OnSendedToAllPlayers();
        }

        protected void ClearSenderState()
        {
            _messageChunks = null;
            _currentChunkIndex = 0;
            _totalChunks = 0;
        }

        protected override void OnReadyCheckCancelled()
        {
            if (IsMessengerOwner())
            {
                ClearSenderState();
                ClearPendingData();
            }

            _targetPlayerIds = new string[0];

            OnSendCancelled();
        }

        public void CancelSend()
        {
            StopReadyCheck();
        }

        protected bool IsLocalPlayerTargeted()
        {
            string playerId = TsPlayerUtils.GetPlayerID(Networking.LocalPlayer);
            return TsArray.Contains(_targetPlayerIds, playerId);
        }

        protected virtual void OnSendStarted(int totalChunks) { }

        protected virtual void OnSendedToAllPlayers() { }

        protected virtual void OnSendFailed(MessageError errorCode) { }

        protected virtual void OnSendCancelled() { }
    }
}
