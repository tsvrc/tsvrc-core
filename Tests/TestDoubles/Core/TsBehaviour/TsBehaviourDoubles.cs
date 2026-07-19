using System.Collections.Generic;
using Tsvrc.Core;
using Tsvrc.Core.Generated;
using Tsvrc.Utils;
using UdonSharp;

namespace Tsvrc.Tests.EditMode
{
    // TsRoot is abstract with only three virtual properties (Instance/Memory/Log), all
    // null by default. TsBehaviour's LogInfo/LogWarning/LogError null-check _ts.Log and
    // fall back to Debug.Log directly when it's null, so a concrete double needs no
    // overrides to exercise TsConstruct(TsRoot).
    public class TestTsRoot : TsRoot
    {
    }

    // Overrides Log so tests can exercise TsBehaviour's LogInfo/LogWarning/LogError
    // delegation path (as opposed to TestTsRoot's fallback-to-Debug.Log path).
    public class TestTsRootWithLogger : TsRoot
    {
        public TsLogger LogOverride;
        public override TsLogger Log => LogOverride;
    }

    public class TsBehaviourTestSubclass : TsBehaviour
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

    // Simulates a Tsvrc framework class (e.g. TsProcess, TsMemory) for tests that need to
    // verify the Internal side of TsLogger's Internal/World level filtering without pulling
    // in a real framework class.
    public class TsInternalBehaviourTestSubclass : TsBehaviour
    {
        protected override bool IsTsvrcInternal => true;

        public void InvokeLogInfo(string message) => LogInfo(message);
        public void InvokeLogWarning(string message) => LogWarning(message);
        public void InvokeLogError(string message) => LogError(message);
    }

    public class TsBehaviourNoDestroySubclass : TsBehaviour
    {
        public int TsDestroyCallCount;

        public override void TsDestroy()
        {
            TsDestroyCallCount++;
        }
    }

    // The optional shared Log lets delivery-order tests assert call order across
    // independent listener instances.
    public class TsListenerDouble : UdonSharpBehaviour
    {
        public List<string> Log;
        public int CallbackACount;
        public int CallbackBCount;

        public void CallbackA()
        {
            CallbackACount++;
            Log?.Add($"{name}.CallbackA");
        }

        public void CallbackB()
        {
            CallbackBCount++;
            Log?.Add($"{name}.CallbackB");
        }
    }

    // CallbackA reentrantly subscribes a fresh listener to the same publisher/event
    // it was invoked from, simulating a mid-TsEmit reentrant TsSubscribe call.
    public class TsReentrantSubscriberDouble : UdonSharpBehaviour
    {
        public TsBehaviour Publisher;
        public string EventName;
        public TsListenerDouble NewListener;
        public int CallbackCount;

        public void CallbackA()
        {
            CallbackCount++;
            Publisher.TsSubscribe(NewListener, EventName, nameof(TsListenerDouble.CallbackA));
        }
    }
}
