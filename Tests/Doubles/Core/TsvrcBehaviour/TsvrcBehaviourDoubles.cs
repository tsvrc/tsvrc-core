using Tsvrc.Core;
using Tsvrc.Core.Generated;
using Tsvrc.Utils;

namespace Tsvrc.Tests.EditMode
{
    // TsRoot is abstract with only three virtual properties (Instance/Memory/Log), all
    // null by default. TsvrcBehaviour's LogInfo/LogWarning/LogError null-check _ts.Log and
    // fall back to Debug.Log directly when it's null, so a concrete double needs no
    // overrides to exercise TsConstruct(TsRoot).
    public class TestTsRoot : TsRoot
    {
    }

    // Overrides Log so tests can exercise TsvrcBehaviour's LogInfo/LogWarning/LogError
    // delegation path (as opposed to TestTsRoot's fallback-to-Debug.Log path).
    public class TestTsRootWithLogger : TsRoot
    {
        public TsvrcLogger LogOverride;
        public override TsvrcLogger Log => LogOverride;
    }

    public class TsvrcBehaviourTestSubclass : TsvrcBehaviour
    {
        public int TsStartCallCount;
        public TsRoot TsAtLastTsStart;

        protected override void TsStart()
        {
            TsStartCallCount++;
            TsAtLastTsStart = _ts;
        }

        public TsRoot GetTs() => _ts;

        public void InvokeLogInfo(string message) => LogInfo(message);
        public void InvokeLogWarning(string message) => LogWarning(message);
        public void InvokeLogError(string message) => LogError(message);
    }

    // Simulates a Tsvrc framework class (e.g. Process, TsvrcMemory) for tests that need to
    // verify the Internal side of TsvrcLogger's Internal/World level filtering without pulling
    // in a real framework class.
    public class TsInternalBehaviourTestSubclass : TsvrcBehaviour
    {
        protected override bool IsTsvrcInternal => true;

        public void InvokeLogInfo(string message) => LogInfo(message);
        public void InvokeLogWarning(string message) => LogWarning(message);
        public void InvokeLogError(string message) => LogError(message);
    }

    public class TsvrcBehaviourNoDestroySubclass : TsvrcBehaviour
    {
        public int TsDestroyCallCount;

        public override void TsDestroy()
        {
            TsDestroyCallCount++;
        }
    }
}
