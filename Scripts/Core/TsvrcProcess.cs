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
            if (!_isRunning || !Networking.IsMaster) return;

            var playerId = TsPlayerUtils.GetPlayerID(player);
            if (playerId == _ownerId)
            {
                SetProcessOwner(Networking.Master);

                // Only the master (new owner) should invoke the event.
                OnOwnerAbandonedProcess();
            }

            OnTsPlayerLeft(player);
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
                SetProcessOwner(Networking.LocalPlayer);
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
                SetProcessOwner(Networking.LocalPlayer);
            }

            _isRunning = false;
            RequestSerialization();

            OnProcessStopped();
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
        }

        /// <summary>
        /// Cancels the Tsvrc Process.
        /// </summary>
        public void CancelProcess()
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

            OnProcessCancelled();
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
        /// Only called on the process owner.
        /// </summary>
        protected virtual void OnProcessStarted() { }

        /// <summary>
        /// Called when the process is stopped.
        /// Only called on the process owner.
        /// </summary>
        protected virtual void OnProcessStopped() { }

        /// <summary>
        /// Called when the process is completed successfully.
        /// Only called on the process owner.
        /// </summary>
        protected virtual void OnProcessCompleted() { }

        /// <summary>
        /// Called when the process is cancelled.
        /// Only called on the process owner.
        /// </summary>
        protected virtual void OnProcessCancelled() { }

        /// <summary>
        /// Called when the process owner leaves the instance and
        /// the process is still running.
        /// This method is called only on the new owner (master).
        /// </summary>
        protected virtual void OnOwnerAbandonedProcess() { }

        /// <summary>
        /// Called when a player leaves the instance.
        /// Only called on the process owner when the process is running.
        /// </summary>
        protected virtual void OnTsPlayerLeft(VRCPlayerApi player) { }

        #endregion
    }
}