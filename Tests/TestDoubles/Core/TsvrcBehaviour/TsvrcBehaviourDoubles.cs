using System.Collections.Generic;
using Tsvrc.Core;
using Tsvrc.Core.Generated;
using UdonSharp;

namespace Tsvrc.Tests.Editor
{
    // TsvrcRoot is abstract with only two virtual properties (Instance/Memory), both
    // null by default. Neither TsvrcBehaviour nor TsvrcProcess dereferences them, so a
    // concrete double needs no overrides to exercise TsConstruct(TsvrcRoot).
    public class TestTsvrcRoot : TsvrcRoot
    {
    }

    public class TsvrcBehaviourTestSubclass : TsvrcBehaviour
    {
        public int TsStartCallCount;
        public TsvrcRoot TsAtLastTsStart;

        protected override void TsStart()
        {
            TsStartCallCount++;
            TsAtLastTsStart = _ts;
        }

        public TsvrcRoot GetTs() => _ts;
    }

    public class TsvrcBehaviourNoDestroySubclass : TsvrcBehaviour
    {
        public int TsDestroyCallCount;

        public override void TsDestroy()
        {
            TsDestroyCallCount++;
        }
    }

    // The optional shared Log lets delivery-order tests assert call order across
    // independent listener instances.
    public class TsvrcListenerDouble : UdonSharpBehaviour
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
    public class TsvrcReentrantSubscriberDouble : UdonSharpBehaviour
    {
        public TsvrcBehaviour Publisher;
        public string EventName;
        public TsvrcListenerDouble NewListener;
        public int CallbackCount;

        public void CallbackA()
        {
            CallbackCount++;
            Publisher.TsSubscribe(NewListener, EventName, nameof(TsvrcListenerDouble.CallbackA));
        }
    }
}
