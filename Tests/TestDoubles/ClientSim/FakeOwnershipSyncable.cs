using UnityEngine;
using VRC.SDK3.ClientSim;

namespace Tsvrc.Testing.Framework
{
    /// <summary>
    /// Without an IClientSimSyncable, ClientSimPlayerManager.IsOwner/SetOwner fall back to
    /// comparing against the instance master, so a GameObject with no synced-object component
    /// always reports the local player as owner in Play Mode. Attaching this gives it a real,
    /// independent owner, letting a test genuinely produce "local player is not the owner".
    /// </summary>
    // FIXME: Add tests proving this.
    public class FakeOwnershipSyncable : MonoBehaviour, IClientSimSyncable
    {
        private int _ownerId;

        public void InitializeOwner(int ownerId)
        {
            _ownerId = ownerId;
        }

        public int GetOwner()
        {
            return _ownerId;
        }

        public void SetOwner(int ownerID)
        {
            _ownerId = ownerID;
        }
    }
}
