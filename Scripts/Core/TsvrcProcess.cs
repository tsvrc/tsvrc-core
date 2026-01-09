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

        protected virtual void Start()
        {
            if (IsProcessOwner())
            {
                // Ensure synced state is correct on start
                SetProcessOwner(Networking.GetOwner(gameObject));
            }
        }

        #endregion

        #region VRChat Callbacks

#pragma warning disable
        public override void OnPlayerLeft(VRCPlayerApi player)
#pragma warning restore
        {
            /* UdonSharp transfers the master before invoking OnPlayerLeft,
             so its safe to check only for master here. */
            if (!IsProcessRunning() || !Networking.IsMaster) return;

            var playerId = TsPlayerUtils.GetPlayerID(player);
            if (playerId == _ownerId)
            {
                SetProcessOwner(Networking.Master);
                OnOwnerAbandonedProcess();
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Starts the Tsvrc Process.
        /// </summary>
        public virtual void StartProcess()
        {
            if (_isRunning)
            {
                Debug.LogWarning("[TsvrcProcess] Process is already running.");
                return;
            }

            if (!IsProcessOwner())
            {
                SetProcessOwner(Networking.LocalPlayer);
            }

            _isRunning = true;
            RequestSerialization();

            OnProcessStarted();
        }

        /// <summary>
        /// Forcibly stops the Tsvrc Process before completion.
        /// </summary>
        public virtual void StopProcess()
        {
            if (!_isRunning)
            {
                Debug.LogWarning("[TsvrcProcess] Process is not running.");
                return;
            }

            if (!IsProcessOwner())
            {
                // Minor FIXME: Setting another owner could cause that a synced variable don't be loaded
                // yet in the new owner, leading to inconsistent states.
                SetProcessOwner(Networking.LocalPlayer);
            }

            _isRunning = false;
            RequestSerialization();

            OnProcessStopped();
            OnProcessCleanup();
        }

        /// <summary>
        /// Completes the Tsvrc Process successfully.
        /// </summary>
        public void CompleteProcess()
        {
            if (!_isRunning)
            {
                Debug.LogWarning("[TsvrcProcess] Process is not running.");
                return;
            }

            if (!IsProcessOwner())
            {
                SetProcessOwner(Networking.LocalPlayer);
            }

            _isRunning = false;
            RequestSerialization();

            OnProcessCompleted();
            OnProcessCleanup();
        }

        /// <summary>
        /// Checks if Tsvrc the process is currently running.
        /// </summary>
        public bool IsProcessRunning()
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
        protected void SetProcessOwner(VRCPlayerApi newOwner)
        {
            _ownerId = TsPlayerUtils.GetPlayerID(newOwner);
            Networking.SetOwner(newOwner, gameObject);
            RequestSerialization();
        }

        /// <summary>
        /// Determines if the local player is the process owner.
        /// </summary>
        protected bool IsProcessOwner()
        {
            return Networking.IsOwner(gameObject);
        }

        #endregion

        #region Virtual Methods

        /// <summary>
        /// Called when the process is started.
        /// Only invoked on the process owner.
        /// </summary>
        protected virtual void OnProcessStarted() { }

        /// <summary>
        /// Called when the process is stopped before completion.
        /// Only invoked on the process owner.
        /// </summary>
        protected virtual void OnProcessStopped() { }

        /// <summary>
        /// Called when the process is completed successfully.
        /// Only invoked on the process owner.
        /// </summary>
        protected virtual void OnProcessCompleted() { }

        /// <summary>
        /// Called when the process owner leaves the instance and the process is still running.
        /// Only invoked on the new owner (typically the master client).
        /// </summary>
        protected virtual void OnOwnerAbandonedProcess() { }

        /// <summary>
        /// Called when a cleanup of the process data is requested.
        /// Only invoked on the process owner.
        /// </summary>
        protected virtual void OnProcessCleanup() { }

        #endregion
    }
}