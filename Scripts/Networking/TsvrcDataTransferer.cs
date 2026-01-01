using Tsvrc.TsNetworking.Utils;
using UdonSharp;

namespace Tsvrc.TsNetworking
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class TsvrcDataTransferer : TsvrcDataReceiver
    {
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
    }
}
