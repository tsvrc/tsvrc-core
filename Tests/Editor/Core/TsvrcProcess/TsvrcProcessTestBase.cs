using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Core;
using UnityEngine;

namespace Tsvrc.Tests.Editor
{
    // Every dual-authority guard in TsvrcProcess is written as
    // `!IsProcessOwner() && !Networking.IsOwner(gameObject)`. IsProcessOwner() is a
    // pure int comparison between two reflection-settable fields with no Networking
    // dependency, so seeding them equal (SeedAsOwner) short-circuits every "local
    // player is the owner" branch without ever touching Networking or constructing a
    // VRCPlayerApi. This is what keeps every class here runnable outside Play Mode.
    // Only the "local player is NOT the owner" forwarding paths and the four VRChat
    // callback overrides need real Networking/VRCPlayerApi; those live in
    // Tests/PlayMode/Core/TsvrcProcess/ instead.
    public abstract class TsvrcProcessTestBase
    {
        protected const int OwnerPlayerId = 777;

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
        }

        protected T CreateProcess<T>(string name = null) where T : TsvrcProcess
        {
            var go = new GameObject(name ?? typeof(T).Name);
            _spawned.Add(go);
            return go.AddComponent<T>();
        }

        protected T CreateComponent<T>(string name = null) where T : Component
        {
            var go = new GameObject(name ?? typeof(T).Name);
            _spawned.Add(go);
            return go.AddComponent<T>();
        }

        // Bypasses TsStart()/SetProcessOwner() (and therefore Networking) entirely.
        protected static void SeedAsOwner(TsvrcProcess process, int playerId = OwnerPlayerId)
        {
            string id = "TestOwner#" + playerId;
            PrivateFieldAccess.SetField(process, "_localPlayerIdInt", playerId);
            PrivateFieldAccess.SetField(process, "_localPlayerId", id);
            PrivateFieldAccess.SetField(process, "_ownerPlayerIdInt", playerId);
            PrivateFieldAccess.SetField(process, "_ownerId", id);
        }

        protected static bool InvokeIsProcessOwner(TsvrcProcess process)
        {
            return (bool)PrivateFieldAccess.InvokeInstance(process, "IsProcessOwner");
        }

        // Synchronous back-to-back _TickProcessUpdate() calls in a test have no real
        // elapsed time between them, so the due-time guard correctly rejects a second
        // immediate call as premature. Tests simulating several legitimate ticks in a
        // row need to explicitly advance past the deadline between them.
        protected static void ForceNextTickDueNow(TsvrcProcess process)
        {
            PrivateFieldAccess.SetField(process, "_nextTickDueAtRealTime", 0f);
        }
    }
}
