using System.Collections;
using System.Collections.Generic;
using Tsvrc.Testing.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode.Core.Process
{
    // ClientSim-backed base for Process PlayMode tests: real VRCPlayerApi identity and
    // ownership routing, neither reachable from EditMode's reflection-based tests.
    public abstract class ProcessPlayModeTestBase : TsPlayModeTestBase
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [UnityTearDown]
        public IEnumerator ProcessPlayModeTestBase_UnityTearDown()
        {
            foreach (GameObject go in _spawned)
                if (go != null)
                    // allowDestroyingAssets: true - [UnityTearDown] order between this base and
                    // derived class isn't guaranteed relative to ClientSim's own teardown, so the
                    // session may already be torn down by the time this runs.
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

        // ClientSimPlayerManager.IsOwner/SetOwner (what Networking routes to under ClientSim)
        // falls back to the instance master when the GameObject has no IClientSimSyncable.
        // FakeOwnershipSyncable gives it a real, independent owner instead, so IsOwner
        // genuinely returns false for the local player - unreachable with a bare AddComponent.
        protected static FakeOwnershipSyncable MakeGenuinelyNotOwner(
            Tsvrc.Core.Process process, VRCPlayerApi otherOwner)
        {
            var syncable = process.gameObject.AddComponent<FakeOwnershipSyncable>();
            syncable.InitializeOwner(otherOwner.playerId);
            return syncable;
        }
    }
}
