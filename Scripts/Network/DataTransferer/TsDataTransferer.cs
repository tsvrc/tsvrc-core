using UdonSharp;

namespace Tsvrc.Network
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class TsDataTransferer : TsDataReceiver
    {
        #region TsvrcProcess Callbacks

        protected override void OnOwnerAbandonedProcess()
        {
            base.OnOwnerAbandonedProcess();

            CancelDataTransfer();
        }

        #endregion

        #region TsvrcDataReceiver Callbacks

        protected override void OnDataReceptionStartedAsTrackedPlayer(string[] playerIds)
        {
            base.OnDataReceptionStartedAsTrackedPlayer(playerIds);

            OnDataTransfererStartedAsTrackedPlayer(playerIds);
        }

        protected override void OnDataReceptionStoppedAsTrackedPlayer(string[] playerIds)
        {
            base.OnDataReceptionStoppedAsTrackedPlayer(playerIds);

            OnDataTransfererStoppedAsTrackedPlayer(playerIds);
        }

        protected override void OnDataReceptionCompletedAsTrackedPlayer(string data, string[] playerIds)
        {
            base.OnDataReceptionCompletedAsTrackedPlayer(data, playerIds);

            OnDataTransfererCompletedAsTrackedPlayer(data, playerIds);
        }

        #endregion

        #region Public Methods

        public override void TransferData(string data, string[] playerIds)
        {
            base.TransferData(data, playerIds);
        }

        public override void CancelDataTransfer()
        {
            base.CancelDataTransfer();
        }

        #endregion

        #region Virtual Methods

        /// <summary>
        /// Called when the data transferer starts on tracked players.
        /// Invoked via network event on all tracked players (non-owners).
        /// This fires once at the beginning of the transfer (first chunk).
        /// For owner-only logic, override OnProcessStarted() from the base class.
        /// </summary>
        protected virtual void OnDataTransfererStartedAsTrackedPlayer(string[] playerIds) { }

        /// <summary>
        /// Called when the data transferer is stopped on tracked players.
        /// Invoked via network event on all tracked players (non-owners).
        /// For owner-only logic, override OnProcessStopped() from the base class.
        /// </summary>
        protected virtual void OnDataTransfererStoppedAsTrackedPlayer(string[] playerIds) { }

        /// <summary>
        /// Called when the data transferer completes on tracked players.
        /// Invoked via network event on all tracked players (non-owners).
        /// This fires once at the end when all chunks are complete.
        /// For owner-only logic, override OnProcessCompleted() from the base class.
        /// </summary>
        /// <param name="data">The complete reassembled data.</param>
        /// <param name="playerIds">The player IDs involved in the transfer.</param>
        protected virtual void OnDataTransfererCompletedAsTrackedPlayer(string data, string[] playerIds) { }

        #endregion
    }
}
