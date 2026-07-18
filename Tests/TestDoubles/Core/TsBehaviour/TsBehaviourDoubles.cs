using System.Collections.Generic;
using Tsvrc.Core;
using Tsvrc.Core.Generated;
using UdonSharp;

namespace Tsvrc.Tests.EditMode
{
    // TsRoot is abstract with only two virtual properties (Instance/Memory), both
    // null by default. Neither TsBehaviour nor TsProcess dereferences them, so a
    // concrete double needs no overrides to exercise TsConstruct(TsRoot).
    public class TestTsRoot : TsRoot
    {
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
