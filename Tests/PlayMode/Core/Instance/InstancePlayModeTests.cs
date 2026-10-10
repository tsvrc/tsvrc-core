using System.Collections;
using NUnit.Framework;
using Tsvrc.UdonTestKit;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode.Core.Instance
{
    // IsTsMaster is a thin wrapper over Networking.IsMaster, which only resolves meaningfully
    // under a live ClientSim session. No test double is needed - a real, unsubclassed Instance
    // is exactly what this checks.
    public class InstancePlayModeTests : ClientSimTestBase
    {
        public InstancePlayModeTests() : base(TestScene.Empty("Tsvrc.Tests.PlayMode.Instance"))
        {
        }

        private static Tsvrc.Core.Instance CreateInstance()
        {
            var instance = new GameObject(nameof(InstancePlayModeTests)).AddComponent<Tsvrc.Core.Instance>();
            instance.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            return instance;
        }

        [UnityTest]
        public IEnumerator IsTsMaster_LocalPlayerIsMaster_MatchesRealNetworkingIsMaster()
        {
            yield return Session.Start(localPlayerIsMaster: true);

            var instance = CreateInstance();

            Assert.AreEqual(Networking.IsMaster, instance.IsTsMaster);
            Assert.IsTrue(instance.IsTsMaster);
        }

        [UnityTest]
        public IEnumerator IsTsMaster_LocalPlayerIsNotMaster_MatchesRealNetworkingIsMaster()
        {
            yield return Session.Start(localPlayerIsMaster: false);

            var instance = CreateInstance();

            Assert.AreEqual(Networking.IsMaster, instance.IsTsMaster);
            Assert.IsFalse(instance.IsTsMaster);
        }
    }
}
