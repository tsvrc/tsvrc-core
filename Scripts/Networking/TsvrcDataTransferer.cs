using Tsvrc.TsNetworking.Utils;
using UdonSharp;

namespace Tsvrc.TsNetworking
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class TsvrcDataTransferer : TsvrcDataReceiver
    {
        #region Tsvrc Callbacks

        protected override void OnOwnerAbandonedProcess()
        {
            base.OnOwnerAbandonedProcess();

            CancelDataTransfer();
        }

        protected override void OnDataReceptionCancelled()
        {
            base.OnDataReceptionCancelled();

            OnDataTranfererProcessCancelled();
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
        /// Called when the data transferer process is cancelled.
        /// This method is intended to be called on the tracker players and the process owner.
        /// Make sure to invoke base.OnDataTranfererProcessCancelled if overridden.
        /// </summary>
        protected virtual void OnDataTranfererProcessCancelled() { }

        #endregion
    }
}
