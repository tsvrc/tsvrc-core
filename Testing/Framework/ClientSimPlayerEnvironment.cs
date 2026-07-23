using System.Collections;
using UnityEngine;
using VRC.SDK3.ClientSim;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace Tsvrc.Testing.Framework
{
    /// <summary>
    /// Real, ClientSim-backed player environment for Play Mode tests: minimal scene
    /// descriptor + ClientSim session setup, spawn/remove/find-by-name against the real
    /// player list, and the teardown ClientSim itself requires. Exposes real VRCPlayerApi
    /// instances directly, since tests that use this environment are specifically verifying
    /// real VRCPlayerApi wiring.
    /// </summary>
    public sealed class ClientSimPlayerEnvironment
    {
        private GameObject _descriptorObject;

        public IEnumerator Start(bool localPlayerIsMaster = true)
        {
#if UNITY_EDITOR
            UnityEditor.SessionState.SetBool("com.vrchat.clientsim.session.accepted_warning", true);
#endif
            CreateMinimalSceneDescriptor();

            var settings = new ClientSimSettings
            {
                enableClientSim = true,
                spawnPlayer = true,
                deleteEditorOnly = false,
                localPlayerIsMaster = localPlayerIsMaster,
                initializationDelay = 0f,
            };

            ClientSimRuntimeLoader.BeginUnityTesting(settings);
            ClientSimRuntimeLoader.StartClientSim(settings);

            yield return null;
            yield return null;
        }

        /// <summary>Spawns a remote player. The spawn is asynchronous - callers yield a couple of frames, then look the player up via FindPlayerByName.</summary>
        public void SpawnRemotePlayer(string displayName)
        {
            ClientSimMain.SpawnRemotePlayer(displayName);
        }

        public void RemovePlayer(VRCPlayerApi player)
        {
            ClientSimMain.RemovePlayer(player);
        }

        public static VRCPlayerApi FindPlayerByName(string name)
        {
            VRCPlayerApi[] players = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
            VRCPlayerApi.GetPlayers(players);
            foreach (VRCPlayerApi p in players)
                if (p != null && p.displayName == name) return p;
            return null;
        }

        public void Teardown()
        {
            if (ClientSimMain.HasInstance())
                ClientSimMain.RemoveInstance();

            ClientSimRuntimeLoader.EndUnityTesting();

            if (_descriptorObject != null)
                Object.DestroyImmediate(_descriptorObject);
            _descriptorObject = null;
        }

        private void CreateMinimalSceneDescriptor()
        {
            _descriptorObject = new GameObject("__TestSceneDescriptor");
            VRCSceneDescriptor descriptor = _descriptorObject.AddComponent<VRCSceneDescriptor>();

            GameObject spawnObject = new GameObject("__TestSpawn");
            spawnObject.transform.SetParent(_descriptorObject.transform);

            descriptor.spawns = new[] { spawnObject.transform };
        }
    }
}
