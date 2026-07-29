using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Testing.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode.Core.Process
{
    // Real, ClientSim-backed base for Process PlayMode tests: real VRCPlayerApi identity and
    // real ownership routing, neither of which reflection-based Edit Mode tests can produce.
    public abstract class ProcessPlayModeTestBase : TsPlayModeTestBase
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [UnityTearDown]
        public IEnumerator ProcessPlayModeTestBase_UnityTearDown()
        {
            foreach (GameObject go in _spawned)
                if (go != null)
                    // allowDestroyingAssets: true - teardown ordering across multiple
                    // [UnityTearDown] methods in this base/derived pair isn't guaranteed relative
                    // to ClientSim's own session teardown, so this GameObject may already be in a
                    // torn-down session context by the time this runs.
                    Object.DestroyImmediate(go, true);
            _spawned.Clear();
            yield return null;
        }

        protected T CreateProcess<T>(string name = null) where T : Tsvrc.Core.Process
        {
            var go = new GameObject(name ?? typeof(T).Name);
            _spawned.Add(go);
            return go.AddComponent<T>();
        }

        // ClientSimPlayerManager.IsOwner/SetOwner - what Networking.IsOwner/SetOwner route to
        // under ClientSim - fall back to comparing against the instance master only when the
        // GameObject has no IClientSimSyncable component. Attaching FakeOwnershipSyncable gives
        // process's GameObject an explicit, independent owner, so Networking.IsOwner(gameObject)
        // genuinely returns false for the local player once otherOwner isn't it - a state a bare
        // AddComponent<Process>() GameObject can never produce in Play Mode on its own.
        protected static FakeOwnershipSyncable MakeGenuinelyNotUnityOwner(
            Tsvrc.Core.Process process, VRCPlayerApi otherOwner)
        {
            var syncable = process.gameObject.AddComponent<FakeOwnershipSyncable>();
            syncable.InitializeOwner(otherOwner.playerId);
            return syncable;
        }
    }
}
