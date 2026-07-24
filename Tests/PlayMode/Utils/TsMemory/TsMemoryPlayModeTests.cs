using System.Collections;
using NUnit.Framework;
using Tsvrc.Testing.Framework;
using Tsvrc.Tests.EditMode;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.Data;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode.Utils.TsMemory
{
    // Set/Remove/Clear's Networking.IsOwner(gameObject)-false ownership-claim branch (synced
    // tier) needs a real GameObject that genuinely isn't Unity-owned by the local player -
    // FakeOwnershipSyncable produces that for real, the same technique
    // Tests/PlayMode/Core/TsProcess/ already uses. OnPlayerRestored + real PlayerData is never
    // exercised anywhere else: it needs a real local VRCPlayerApi, and ClientSim never
    // organically dispatches it to a plain UdonSharpBehaviour, so it is invoked directly below.
    public class TsMemoryPlayModeTests : TsPlayModeTestBase
    {
        private GameObject _gameObject;

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null) Object.DestroyImmediate(_gameObject);
        }

        [UnityTest]
        public IEnumerator Set_SyncedKeyWhileGenuinelyNotUnityOwner_ClaimsRealOwnership()
        {
            yield return StartClientSim();
            Players.SpawnRemotePlayer("OtherOwner");
            yield return null;
            yield return null;
            VRCPlayerApi otherOwner = ClientSimPlayerEnvironment.FindPlayerByName("OtherOwner");
            Assert.IsNotNull(otherOwner, "Setup sanity check: OtherOwner was not found.");

            _gameObject = new GameObject(nameof(TsMemoryPlayModeTests));
            var memory = _gameObject.AddComponent<Tsvrc.Utils.TsMemory>();
            memory.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            var syncable = _gameObject.AddComponent<FakeOwnershipSyncable>();
            syncable.InitializeOwner(otherOwner.playerId);

            memory.Register("key", persist: false, synced: true);
            Assert.IsFalse(Networking.IsOwner(_gameObject),
                "Setup sanity check: the local player must genuinely not be the Unity owner.");

            memory.Set("key", new DataToken("value"));

            Assert.IsTrue(Networking.IsOwner(_gameObject),
                "Set on a synced key must claim real Unity ownership when the local player isn't already the owner.");
        }

        [UnityTest]
        public IEnumerator OnPlayerRestored_RealLocalPlayer_LoadsPersistValueFromRealPlayerData()
        {
            yield return StartClientSim();

            _gameObject = new GameObject(nameof(TsMemoryPlayModeTests));
            var memory = _gameObject.AddComponent<Tsvrc.Utils.TsMemory>();
            memory.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);

            memory.Register("score", persist: true, synced: false);
            memory.Add("score", new DataToken(0));
            memory.OnPlayerRestored(Networking.LocalPlayer);
            memory.Set("score", new DataToken(42));

            // Simulate a fresh session's restore against the same real PlayerData.
            PrivateFieldAccess.SetField(memory, "_playerRestored", false);
            PrivateFieldAccess.GetField<DataDictionary>(memory, "_persistStore").Clear();

            memory.OnPlayerRestored(Networking.LocalPlayer);

            Assert.IsTrue(memory.IsPlayerRestored);
            Assert.AreEqual(42, memory.GetInt("score"),
                "OnPlayerRestored must load the value back from real PlayerData, not the local cache.");
        }
    }
}
