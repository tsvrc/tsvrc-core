using System.Collections;
using NUnit.Framework;
using Tsvrc.UdonTestKit;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.Data;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode.Utils.TsvrcMemory
{
    // Set/Remove/Clear's Networking.IsOwner(gameObject)-false ownership-claim branch (synced
    // tier) needs a real GameObject that genuinely isn't Unity-owned by the local player -
    // Session.SetOwner produces that for real. OnPlayerRestored + real PlayerData needs a
    // real local VRCPlayerApi, and ClientSim never organically dispatches it to a plain
    // UdonSharpBehaviour, so it is invoked directly below.
    public class TsMemoryPlayModeTests : ClientSimTestBase
    {
        public TsMemoryPlayModeTests() : base(TestScene.Empty("Tsvrc.Tests.PlayMode.TsvrcMemory"))
        {
        }

        private static Tsvrc.Utils.TsvrcMemory CreateMemory()
        {
            var memory = new GameObject(nameof(TsMemoryPlayModeTests)).AddComponent<Tsvrc.Utils.TsvrcMemory>();
            memory.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            return memory;
        }

        [UnityTest]
        public IEnumerator Set_SyncedKeyWhileGenuinelyNotUnityOwner_ClaimsRealOwnership()
        {
            yield return Session.Start();
            VRCPlayerApi otherOwner = Session.SpawnRemotePlayer("OtherOwner");
            var memory = CreateMemory();
            Session.SetOwner(otherOwner, memory.gameObject);
            memory.Register("key", persist: false, synced: true);

            memory.Set("key", new DataToken("value"));

            Assert.IsTrue(Networking.IsOwner(memory.gameObject),
                "Set on a synced key must claim real Unity ownership when the local player isn't already the owner.");
        }

        [UnityTest]
        public IEnumerator OnPlayerRestored_AfterARealRejoin_LoadsPersistValueFromRealPlayerData()
        {
            ClientSimSession.DeleteSavedData();
            yield return Session.Start();
            var memory = CreateMemory();
            memory.Register("score", persist: true, synced: false);
            memory.Add("score", new DataToken(0));
            memory.OnPlayerRestored(Networking.LocalPlayer);
            memory.Set("score", new DataToken(42));
            yield return null;

            Session.End();
            yield return Session.Start();
            var rejoined = CreateMemory();
            rejoined.Register("score", persist: true, synced: false);
            rejoined.Add("score", new DataToken(0));
            rejoined.OnPlayerRestored(Networking.LocalPlayer);

            Assert.IsTrue(rejoined.IsPlayerRestored);
            Assert.AreEqual(42, rejoined.GetInt("score"),
                "OnPlayerRestored must load the value saved before the player left from real PlayerData.");
        }
    }
}
