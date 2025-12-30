using System.ComponentModel;
using Tsvrc.TsNetworking.Utils;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Tsvrc.Core
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class TsvrcProcess : UdonSharpBehaviour
    {
        [UdonSynced] private bool _isRunning = false;
        [UdonSynced] private string _ownerId = "";

        #region Unity Lifecycle

        private void Start()
        {
            if (IsProcessOwner())
            {
                // Ensure synced state is correct on start
                SetOwner(Networking.GetOwner(gameObject));
            }
        }

        #endregion

        #region VRChat Callbacks

#pragma warning disable
        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void OnPlayerLeft(VRCPlayerApi player)
#pragma warning restore
        {
            /* UdonSharp transfers the master before invoking OnPlayerLeft,
             so its safe to check only for master here. */
            if (!_isRunning || !Networking.IsMaster) return;

            var playerId = TsPlayerUtils.GetPlayerID(player);
            if (playerId == _ownerId)
            {
                SetOwner(Networking.Master);

                // Only the master (new owner) should invoke the event.
                OnOwnerAbandonedProcess();
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Starts the Tsvrc Process.
        /// </summary>
        public void StartProcess()
        {
            if (_isRunning)
            {
                Debug.LogWarning("[TsvrcProcess] Process is already running.");
                return;
            }

            if (!IsProcessOwner())
            {
                SetOwner(Networking.LocalPlayer);
            }

            _isRunning = true;
            RequestSerialization();

            OnProcessStarted();
        }

        /// <summary>
        /// Stops the Tsvrc Process.
        /// </summary>
        public void StopProcess()
        {
            if (!_isRunning)
            {
                Debug.LogWarning("[TsvrcProcess] Process is not running.");
                return;
            }

            if (!IsProcessOwner())
            {
                SetOwner(Networking.LocalPlayer);
            }

            _isRunning = false;
            RequestSerialization();

            OnProcessStopped();
        }

        /// <summary>
        /// Checks if the process is currently running.
        /// </summary>
        public bool IsRunning()
        {
            return _isRunning;
        }

        #endregion

        #region Protected Methods

        /// <summary>
        /// Sets the owner of the process.
        /// Use this method instead of SetOwner to ensure
        /// the internal state is updated correctly.
        /// </summary>
        protected void SetOwner(VRCPlayerApi newOwner)
        {
            _ownerId = TsPlayerUtils.GetPlayerID(newOwner);
            Networking.SetOwner(newOwner, gameObject);
            RequestSerialization();
        }

        #endregion

        #region Virtual Methods

        /// <summary>
        /// Called when the process is started.
        /// Only called on the process owner.
        /// </summary>
        public virtual void OnProcessStarted() { }

        /// <summary>
        /// Called when the process is stopped.
        /// Only called on the process owner.
        /// </summary>
        public virtual void OnProcessStopped() { }

        /// <summary>
        /// Called when the process owner leaves the instance and
        /// the process is still running.
        /// This method is called only on the new owner (master).
        /// </summary>
        public virtual void OnOwnerAbandonedProcess() { }

        #endregion

        #region Network Events
        #endregion

        #region Private Methods
        private bool IsProcessOwner()
        {
            return Networking.IsOwner(gameObject);
        }
        #endregion
    }
}