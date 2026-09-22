using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Tsvrc.Core;
using Tsvrc.Testing.Framework;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // A bare AddComponent<Process>() GameObject has no owner set, so Networking.IsOwner defaults
    // true outside Play Mode - IsProcessOwner() is true here with no seeding needed. The "not
    // owner" paths, and anything touching Networking.LocalPlayer (null here), live in
    // Tests/PlayMode/Core/Process/ instead.
    public abstract class ProcessTestBase
    {
        // FIXME: No-op shim kept only so PlayerTracker/ReadyCheckProcess/TsvrcTimer/DataTransfer/
        // RankedGameSession tests still compile. Don't call this from new Process tests.
        protected const int OwnerPlayerId = 777;

        protected static void SeedAsOwner(Process process, int playerId = OwnerPlayerId) { }

        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
                if (go != null)
                    UnityEngine.Object.DestroyImmediate(go);

            _spawned.Clear();
        }

        protected T CreateProcess<T>(string name = null) where T : Process
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

        // Back-to-back _TickProcessUpdate() calls have no real elapsed time between them, so the
        // due-time guard rejects the second as premature unless this forces the deadline past.
        protected static void ForceNextTickDueNow(Process process)
        {
            PrivateFieldAccess.SetField(process, "_nextTickDueAtRealTime", 0f);
        }

        // Invokes a VRC callback (OnPlayerLeft, OnOwnershipTransferred, ...) by reflection, since
        // VRCPlayerApi isn't usable here and GetMethod(name) alone is ambiguous. Passing null is
        // fine: these callbacks guards ignore the parameter outside Play Mode.
        protected static void InvokeVrcPlayerCallback(Process process, string methodName)
        {
            var playerApiType = Type.GetType("VRC.SDKBase.VRCPlayerApi, VRCSDKBase");
            Assert.IsNotNull(playerApiType, "VRCPlayerApi type not found. Assembly name changed?");
            var method = process.GetType().GetMethod(methodName,
                BindingFlags.Public | BindingFlags.Instance, null, new[] { playerApiType }, null);
            Assert.IsNotNull(method, $"{methodName}(VRCPlayerApi) not found. Signature changed?");
            method.Invoke(process, new object[] { null });
        }
    }
}
