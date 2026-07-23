using UnityEngine;
using VRC.SDK3.ClientSim;

namespace Tsvrc.Testing.Framework
{
    /// <summary>
    /// ClientSimPlayerManager.IsOwner/SetOwner (what Networking.IsOwner/SetOwner route to
    /// under ClientSim) fall back to comparing against the instance master only when the
    /// target GameObject has no IClientSimSyncable component - a plain UdonSharpBehaviour with
    /// no synced-object component of its own therefore always reports the local player as
    /// owner in Play Mode. Attaching this component gives the GameObject an explicit,
    /// independently settable owner instead, so a test can genuinely produce
    /// "the local player is not the owner" - a state otherwise unreachable in real Play Mode.
    /// Lives in Tsvrc.Tests.Doubles rather than Tsvrc.Testing.Framework itself: that assembly
    /// is Editor-only, and Unity refuses AddComponent for any MonoBehaviour compiled into an
    /// Editor-only assembly regardless of the class's own contents - the same reason
    /// TsProcessDoubles.cs's [UdonBehaviourSyncMode] doubles live here instead of inline.
    /// </summary>
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
