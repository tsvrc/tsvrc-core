using Tsvrc.UdonTestKit;
using UnityEngine;

namespace Tsvrc.Tests.PlayMode.Core.Process
{
    // ClientSim-backed base for Process PlayMode tests: real VRCPlayerApi identity and
    // ownership routing, neither reachable from EditMode's reflection-based tests. Every test
    // runs in an empty scene of its own, so the processes it creates go when the scene unloads.
    public abstract class ProcessPlayModeTestBase : ClientSimTestBase
    {
        protected ProcessPlayModeTestBase() : base(TestScene.Empty("Tsvrc.Tests.PlayMode.Process"))
        {
        }

        protected static T CreateProcess<T>(string name = null) where T : Tsvrc.Core.Process
        {
            return new GameObject(name ?? typeof(T).Name).AddComponent<T>();
        }
    }
}
