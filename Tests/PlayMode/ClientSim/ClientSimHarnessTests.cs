using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.ClientSim;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode
{
    // Proof-of-concept for Plan step 0.2.6: confirms the ClientSim harness itself
    // works in this project before anything in Phase 5 is built on top of it.
    // VRC.ClientSim ships no public test-base class (the `VRC.ClientSim.Tests`
    // assembly referenced by its own Samples~ is not present in this SDK version),
    // so this drives ClientSimRuntimeLoader directly instead.
    //
    // Play Mode tests run in an isolated blank scene, not whatever scene is open
    // in the Editor, so ClientSim's "no scene descriptor" requirement (every VRChat
    // world needs a VRCSceneDescriptor with at least one spawn point) has to be
    // built here rather than relying on a real scene asset.
    public class ClientSimHarnessTests
    {
        private GameObject _descriptorObject;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (ClientSimMain.HasInstance())
                ClientSimMain.RemoveInstance();

            ClientSimRuntimeLoader.EndUnityTesting();

            if (_descriptorObject != null)
                Object.DestroyImmediate(_descriptorObject);

            yield return null;
        }

        private void CreateMinimalSceneDescriptor()
        {
            _descriptorObject = new GameObject("__TestSceneDescriptor");
            VRCSceneDescriptor descriptor = _descriptorObject.AddComponent<VRCSceneDescriptor>();

            GameObject spawnObject = new GameObject("__TestSpawn");
            spawnObject.transform.SetParent(_descriptorObject.transform);

            descriptor.spawns = new[] { spawnObject.transform };
        }

        [UnityTest]
        public IEnumerator ClientSim_Starts_AndSpawnsValidLocalPlayer()
        {
            // Suppress ClientSim's one-time "VRChat Client Simulator" disclaimer
            // dialog, which otherwise blocks Play Mode indefinitely waiting for a
            // manual Accept click and leaves the test stuck on InitTestScene.
#if UNITY_EDITOR
            UnityEditor.SessionState.SetBool("com.vrchat.clientsim.session.accepted_warning", true);
#endif

            CreateMinimalSceneDescriptor();

            ClientSimSettings settings = new ClientSimSettings
            {
                enableClientSim = true,
                spawnPlayer = true,
                deleteEditorOnly = false,
                localPlayerIsMaster = true,
                initializationDelay = 0f,
            };

            ClientSimRuntimeLoader.BeginUnityTesting(settings);
            ClientSimRuntimeLoader.StartClientSim(settings);

            yield return null;
            yield return null;

            Assert.IsTrue(ClientSimMain.HasInstance(), "ClientSim did not start.");
            Assert.IsNotNull(Networking.LocalPlayer, "Local player was not spawned.");
            Assert.IsTrue(Networking.LocalPlayer.IsValid(), "Local player is not valid.");
        }
    }
}
