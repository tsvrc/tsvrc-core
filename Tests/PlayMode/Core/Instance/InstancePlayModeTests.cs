using System.Collections;
using NUnit.Framework;
using Tsvrc.Testing.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode.Core.Instance
{
    // IsTsMaster is a thin wrapper over Networking.IsMaster, which only resolves meaningfully
    // under a live ClientSim session. No test double is needed - a real, unsubclassed Instance
    // is exactly what this checks.
    public class InstancePlayModeTests : TsPlayModeTestBase
    {
        private GameObject _gameObject;

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null) Object.DestroyImmediate(_gameObject);
        }

        [UnityTest]
        public IEnumerator IsTsMaster_LocalPlayerIsMaster_MatchesRealNetworkingIsMaster()
        {
            yield return StartClientSim(localPlayerIsMaster: true);

            _gameObject = new GameObject(nameof(InstancePlayModeTests));
            var instance = _gameObject.AddComponent<Tsvrc.Core.Instance>();
            instance.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);

            Assert.AreEqual(Networking.IsMaster, instance.IsTsMaster);
            Assert.IsTrue(instance.IsTsMaster);
        }

        [UnityTest]
        public IEnumerator IsTsMaster_LocalPlayerIsNotMaster_MatchesRealNetworkingIsMaster()
        {
            yield return StartClientSim(localPlayerIsMaster: false);

            _gameObject = new GameObject(nameof(InstancePlayModeTests));
            var instance = _gameObject.AddComponent<Tsvrc.Core.Instance>();
            instance.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);

            Assert.AreEqual(Networking.IsMaster, instance.IsTsMaster);
            Assert.IsFalse(instance.IsTsMaster);
        }
    }
}
