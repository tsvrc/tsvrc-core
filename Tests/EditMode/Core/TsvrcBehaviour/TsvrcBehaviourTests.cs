using NUnit.Framework;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System;
using Tsvrc.Core.Generated;
using Tsvrc.Core;
using Tsvrc.Testing.Framework;
using UnityEngine.TestTools;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // TsvrcBehaviour never touches Networking/VRCPlayerApi, so nothing here needs
    // ClientSim or Play Mode.
    public class TsvrcBehaviourTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
                if (go != null)
                    UnityEngine.Object.DestroyImmediate(go);

            _spawned.Clear();
        }

        private T CreateBehaviour<T>(string name = null) where T : UdonSharp.UdonSharpBehaviour
        {
            var go = new GameObject(name ?? typeof(T).Name);
            _spawned.Add(go);
            return go.AddComponent<T>();
        }

        [Test]
        public void TsConstruct_Root_AssignsTs()
        {
            var behaviour = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            var root = CreateBehaviour<TestTsRoot>();

            behaviour.TsConstruct(root);

            Assert.AreSame(root, behaviour.GetTs());
        }

        [Test]
        public void TsConstruct_Root_CallsTsStartExactlyOnce()
        {
            var behaviour = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            var root = CreateBehaviour<TestTsRoot>();

            behaviour.TsConstruct(root);

            Assert.AreEqual(1, behaviour.TsStartCallCount);
        }

        [Test]
        public void TsConstruct_NullRoot_AssignsNullWithoutThrowing()
        {
            var behaviour = CreateBehaviour<TsvrcBehaviourTestSubclass>();

            Assert.DoesNotThrow(() => behaviour.TsConstruct((TsRoot)null));
            Assert.IsNull(behaviour.GetTs());
            Assert.AreEqual(1, behaviour.TsStartCallCount);
        }

        [Test]
        public void TsConstruct_Root_CalledTwice_SecondCallIsANoOp()
        {
            // A second TsConstruct call must not reassign _ts or re-run TsStart(), so
            // a subclass's one-time setup in TsStart() can't be silently re-executed.
            var behaviour = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            var rootA = CreateBehaviour<TestTsRoot>();
            var rootB = CreateBehaviour<TestTsRoot>();

            behaviour.TsConstruct(rootA);
            behaviour.TsConstruct(rootB);

            Assert.AreEqual(1, behaviour.TsStartCallCount);
            Assert.AreSame(rootA, behaviour.GetTs());
        }

        [Test]
        public void TsConstruct_Parent_CalledTwice_SecondCallIsANoOp()
        {
            var parentA = CreateBehaviour<TsvrcBehaviourTestSubclass>("ParentA");
            var parentB = CreateBehaviour<TsvrcBehaviourTestSubclass>("ParentB");
            var rootA = CreateBehaviour<TestTsRoot>();
            var rootB = CreateBehaviour<TestTsRoot>();
            parentA.TsConstruct(rootA);
            parentB.TsConstruct(rootB);
            var child = CreateBehaviour<TsvrcBehaviourTestSubclass>("Child");

            child.TsConstruct(parentA);
            child.TsConstruct(parentB);

            Assert.AreEqual(1, child.TsStartCallCount);
            Assert.AreSame(rootA, child.GetTs());
        }

        [Test]
        public void TsConstruct_Root_AssignsTsBeforeTsStartRuns()
        {
            var behaviour = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            var root = CreateBehaviour<TestTsRoot>();

            behaviour.TsConstruct(root);

            Assert.AreSame(root, behaviour.TsAtLastTsStart);
        }

        [Test]
        public void TsConstruct_Parent_PropagatesSameTsReference()
        {
            var parent = CreateBehaviour<TsvrcBehaviourTestSubclass>("Parent");
            var root = CreateBehaviour<TestTsRoot>();
            parent.TsConstruct(root);

            var child = CreateBehaviour<TsvrcBehaviourTestSubclass>("Child");
            child.TsConstruct(parent);

            Assert.AreSame(root, child.GetTs());
        }

        [Test]
        public void TsConstruct_Parent_CallsTsStartOnChildOnly()
        {
            var parent = CreateBehaviour<TsvrcBehaviourTestSubclass>("Parent");
            var root = CreateBehaviour<TestTsRoot>();
            parent.TsConstruct(root);
            int parentCallsAfterOwnConstruct = parent.TsStartCallCount;

            var child = CreateBehaviour<TsvrcBehaviourTestSubclass>("Child");
            child.TsConstruct(parent);

            Assert.AreEqual(1, child.TsStartCallCount);
            Assert.AreEqual(parentCallsAfterOwnConstruct, parent.TsStartCallCount);
        }

        [Test]
        public void TsConstruct_NullParent_ThrowsNullReferenceException()
        {
            var child = CreateBehaviour<TsvrcBehaviourTestSubclass>();

            Assert.Throws<NullReferenceException>(() => child.TsConstruct((TsvrcBehaviour)null));
        }

        [Test]
        public void TsConstruct_Parent_AlreadyConstructed_SecondCallWithNullParent_IsANoOpAndDoesNotThrow()
        {
            // The _isConstructed guard runs before parent is ever dereferenced, so a
            // repeat call is a no-op even when parent is null on that later call.
            var root = CreateBehaviour<TestTsRoot>();
            var parent = CreateBehaviour<TsvrcBehaviourTestSubclass>("Parent");
            parent.TsConstruct(root);
            var child = CreateBehaviour<TsvrcBehaviourTestSubclass>("Child");
            child.TsConstruct(parent);

            Assert.DoesNotThrow(() => child.TsConstruct((TsvrcBehaviour)null));
            Assert.AreEqual(1, child.TsStartCallCount);
            Assert.AreSame(root, child.GetTs());
        }

        [Test]
        public void TsConstruct_ParentNeverConstructed_PropagatesNullWithoutThrowing()
        {
            var parent = CreateBehaviour<TsvrcBehaviourTestSubclass>("Parent");
            var child = CreateBehaviour<TsvrcBehaviourTestSubclass>("Child");

            Assert.DoesNotThrow(() => child.TsConstruct(parent));
            Assert.IsNull(child.GetTs());
        }

        [Test]
        public void TsConstruct_Parent_ChainedThroughMultipleHops_PropagatesOriginalRoot()
        {
            var root = CreateBehaviour<TestTsRoot>();
            var a = CreateBehaviour<TsvrcBehaviourTestSubclass>("A");
            a.TsConstruct(root);
            var b = CreateBehaviour<TsvrcBehaviourTestSubclass>("B");
            b.TsConstruct(a);
            var c = CreateBehaviour<TsvrcBehaviourTestSubclass>("C");

            c.TsConstruct(b);

            Assert.AreSame(root, c.GetTs());
        }

        [Test]
        public void TsSubscribe_Single_AppendsOneEntryToAllThreeArrays()
        {
            var publisher = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            var listener = CreateBehaviour<TsListenerDouble>();

            publisher.TsSubscribe(listener, "Ev", nameof(TsListenerDouble.CallbackA));

            var listeners = PrivateFieldAccess.GetField<UdonSharp.UdonSharpBehaviour[]>(publisher, "_subListeners");
            var keys = PrivateFieldAccess.GetField<string[]>(publisher, "_subKeys");
            var callbacks = PrivateFieldAccess.GetField<string[]>(publisher, "_subCallbacks");

            // The backing arrays are pre-grown capacity, not exact-size, so only
            // _subCount and the populated slots are part of the contract; array Length
            // is an implementation detail.
            Assert.AreEqual(1, PrivateFieldAccess.GetField<int>(publisher, "_subCount"));
            Assert.AreSame(listener, listeners[0]);
            Assert.AreEqual("Ev", keys[0]);
            Assert.AreEqual(nameof(TsListenerDouble.CallbackA), callbacks[0]);
        }

        [Test]
        public void TsSubscribe_SameListenerTwiceForSameEvent_DeliversCallbackTwice()
        {
            var publisher = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            var listener = CreateBehaviour<TsListenerDouble>();

            publisher.TsSubscribe(listener, "Ev", nameof(TsListenerDouble.CallbackA));
            publisher.TsSubscribe(listener, "Ev", nameof(TsListenerDouble.CallbackA));
            publisher.TsEmit("Ev");

            Assert.AreEqual(2, listener.CallbackACount);
        }

        [Test]
        public void TsSubscribe_SameListenerDifferentEvents_BothRecorded()
        {
            var publisher = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            var listener = CreateBehaviour<TsListenerDouble>();

            publisher.TsSubscribe(listener, "EvA", nameof(TsListenerDouble.CallbackA));
            publisher.TsSubscribe(listener, "EvB", nameof(TsListenerDouble.CallbackB));
            publisher.TsEmit("EvA");
            publisher.TsEmit("EvB");

            Assert.AreEqual(1, listener.CallbackACount);
            Assert.AreEqual(1, listener.CallbackBCount);
        }

        [Test]
        public void TsSubscribe_NullListener_DoesNotThrowAtSubscribeTime()
        {
            var publisher = CreateBehaviour<TsvrcBehaviourTestSubclass>();

            Assert.DoesNotThrow(() => publisher.TsSubscribe(null, "Ev", "Callback"));
        }

        [Test]
        public void TsSubscribe_NullAndEmptyEventName_MatchOrdinaryStringEquality()
        {
            var publisher = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            var listenerForNull = CreateBehaviour<TsListenerDouble>("ListenerNull");
            var listenerForEmpty = CreateBehaviour<TsListenerDouble>("ListenerEmpty");

            publisher.TsSubscribe(listenerForNull, null, nameof(TsListenerDouble.CallbackA));
            publisher.TsSubscribe(listenerForEmpty, "", nameof(TsListenerDouble.CallbackA));

            publisher.TsEmit(null);
            publisher.TsEmit("");

            Assert.AreEqual(1, listenerForNull.CallbackACount);
            Assert.AreEqual(1, listenerForEmpty.CallbackACount);
        }

        [Test]
        public void TsSubscribe_GrowsCapacityByDoublingInsteadOfReallocatingEveryCall()
        {
            var publisher = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            var listenerA = CreateBehaviour<TsListenerDouble>("A");
            var listenerB = CreateBehaviour<TsListenerDouble>("B");
            var filler = CreateBehaviour<TsListenerDouble>("Filler");
            var overflow = CreateBehaviour<TsListenerDouble>("Overflow");

            publisher.TsSubscribe(listenerA, "Ev", nameof(TsListenerDouble.CallbackA));
            var afterFirst = PrivateFieldAccess.GetField<UdonSharp.UdonSharpBehaviour[]>(publisher, "_subListeners");

            // Subscribing again while capacity remains must reuse the same array.
            publisher.TsSubscribe(listenerB, "Ev", nameof(TsListenerDouble.CallbackA));
            var afterSecond = PrivateFieldAccess.GetField<UdonSharp.UdonSharpBehaviour[]>(publisher, "_subListeners");
            Assert.AreSame(afterFirst, afterSecond, "Subscribing within existing capacity must not reallocate.");

            // Fill remaining capacity, then subscribe once more to force a grow.
            while (PrivateFieldAccess.GetField<int>(publisher, "_subCount") < afterSecond.Length)
                publisher.TsSubscribe(filler, "Filler", nameof(TsListenerDouble.CallbackA));
            var full = PrivateFieldAccess.GetField<UdonSharp.UdonSharpBehaviour[]>(publisher, "_subListeners");

            publisher.TsSubscribe(overflow, "Ev", nameof(TsListenerDouble.CallbackA));
            var afterGrow = PrivateFieldAccess.GetField<UdonSharp.UdonSharpBehaviour[]>(publisher, "_subListeners");

            Assert.AreNotSame(full, afterGrow, "Subscribing beyond capacity must reallocate.");
            Assert.Greater(afterGrow.Length, full.Length);
        }

        [Test]
        public void TsSubscribe_CapacityGrowth_PreservesAlreadySubscribedEntries()
        {
            var publisher = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            var listeners = new TsListenerDouble[10];
            for (int i = 0; i < listeners.Length; i++)
            {
                listeners[i] = CreateBehaviour<TsListenerDouble>("L" + i);
                publisher.TsSubscribe(listeners[i], "Ev", nameof(TsListenerDouble.CallbackA));
            }

            publisher.TsEmit("Ev");

            foreach (var listener in listeners)
                Assert.AreEqual(1, listener.CallbackACount);
        }

        [Test]
        public void TsEmit_NoSubscribers_DoesNotThrow()
        {
            var publisher = CreateBehaviour<TsvrcBehaviourTestSubclass>();

            Assert.DoesNotThrow(() => publisher.TsEmit("Ev"));
        }

        [Test]
        public void TsEmit_OneMatchingSubscriber_InvokesCallbackExactlyOnce()
        {
            var publisher = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            var listener = CreateBehaviour<TsListenerDouble>();
            publisher.TsSubscribe(listener, "Ev", nameof(TsListenerDouble.CallbackA));

            publisher.TsEmit("Ev");

            Assert.AreEqual(1, listener.CallbackACount);
        }

        [Test]
        public void TsEmit_MultipleSubscribers_AllInvoked()
        {
            var publisher = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            var listenerA = CreateBehaviour<TsListenerDouble>("A");
            var listenerB = CreateBehaviour<TsListenerDouble>("B");
            publisher.TsSubscribe(listenerA, "Ev", nameof(TsListenerDouble.CallbackA));
            publisher.TsSubscribe(listenerB, "Ev", nameof(TsListenerDouble.CallbackA));

            publisher.TsEmit("Ev");

            Assert.AreEqual(1, listenerA.CallbackACount);
            Assert.AreEqual(1, listenerB.CallbackACount);
        }

        [Test]
        public void TsEmit_DifferentEvent_DoesNotCrossInvoke()
        {
            var publisher = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            var listener = CreateBehaviour<TsListenerDouble>();
            publisher.TsSubscribe(listener, "EventY", nameof(TsListenerDouble.CallbackA));

            publisher.TsEmit("EventX");

            Assert.AreEqual(0, listener.CallbackACount);
        }

        [Test]
        public void TsEmit_DeliveryOrder_MatchesSubscriptionOrder()
        {
            var publisher = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            var log = new List<string>();
            var listenerA = CreateBehaviour<TsListenerDouble>("First");
            var listenerB = CreateBehaviour<TsListenerDouble>("Second");
            listenerA.Log = log;
            listenerB.Log = log;
            publisher.TsSubscribe(listenerA, "Ev", nameof(TsListenerDouble.CallbackA));
            publisher.TsSubscribe(listenerB, "Ev", nameof(TsListenerDouble.CallbackA));

            publisher.TsEmit("Ev");

            Assert.AreEqual(new[] { "First.CallbackA", "Second.CallbackA" }, log.ToArray());
        }

        [Test]
        public void TsEmit_ListenerNullAtSubscribeTime_ThrowsNullReferenceException()
        {
            var publisher = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            publisher.TsSubscribe(null, "Ev", "Callback");

            Assert.Throws<NullReferenceException>(() => publisher.TsEmit("Ev"));
        }

        [Test]
        public void TsEmit_ListenerNullAtSubscribeTime_EarlierListenersStillReceivedBeforeThrow()
        {
            var publisher = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            var goodListener = CreateBehaviour<TsListenerDouble>();
            publisher.TsSubscribe(goodListener, "Ev", nameof(TsListenerDouble.CallbackA));
            publisher.TsSubscribe(null, "Ev", "Callback");

            Assert.Throws<NullReferenceException>(() => publisher.TsEmit("Ev"));
            Assert.AreEqual(1, goodListener.CallbackACount);
        }

        [Test]
        public void TsEmit_ReentrantSubscribeDuringEmit_NewSubscriberNotInvokedThisEmit()
        {
            // TsEmit snapshots the arrays before iterating, and TsSubscribe never
            // mutates in place, so a subscription added mid-emit is not invoked until
            // the next TsEmit call.
            var publisher = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            var newListener = CreateBehaviour<TsListenerDouble>("New");
            var reentrant = CreateBehaviour<TsReentrantSubscriberDouble>("Reentrant");
            reentrant.Publisher = publisher;
            reentrant.EventName = "Ev";
            reentrant.NewListener = newListener;
            publisher.TsSubscribe(reentrant, "Ev", nameof(TsReentrantSubscriberDouble.CallbackA));

            publisher.TsEmit("Ev");

            Assert.AreEqual(1, reentrant.CallbackCount);
            Assert.AreEqual(0, newListener.CallbackACount);

            publisher.TsEmit("Ev");

            Assert.AreEqual(1, newListener.CallbackACount);
        }

        [Test]
        public void TsEmit_UnconstructedBehaviour_StillWorks()
        {
            var publisher = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            var listener = CreateBehaviour<TsListenerDouble>();
            publisher.TsSubscribe(listener, "Ev", nameof(TsListenerDouble.CallbackA));

            Assert.DoesNotThrow(() => publisher.TsEmit("Ev"));
            Assert.AreEqual(1, listener.CallbackACount);
        }

        [Test]
        public void TsEmit_ListenerGameObjectDestroyedBeforeEmit_IsSkippedSilently()
        {
            // A listener destroyed after subscribing is skipped instead of receiving a
            // callback on a dead object — distinct from a listener that was null at
            // subscribe time, which still throws (see TsEmit_ListenerNullAtSubscribeTime_
            // ThrowsNullReferenceException above).
            var publisher = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            var listenerGo = new GameObject("DestroyedListener");
            _spawned.Add(listenerGo);
            var listener = listenerGo.AddComponent<TsListenerDouble>();
            publisher.TsSubscribe(listener, "Ev", nameof(TsListenerDouble.CallbackA));

            UnityEngine.Object.DestroyImmediate(listenerGo);

            Assert.DoesNotThrow(() => publisher.TsEmit("Ev"));
            Assert.AreEqual(0, listener.CallbackACount);
        }

        [Test]
        public void TsEmit_DestroyedListenerAmongOthers_OthersStillReceiveCallback()
        {
            var publisher = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            var listenerGo = new GameObject("DestroyedListener");
            _spawned.Add(listenerGo);
            var destroyedListener = listenerGo.AddComponent<TsListenerDouble>();
            var aliveListener = CreateBehaviour<TsListenerDouble>("Alive");
            publisher.TsSubscribe(destroyedListener, "Ev", nameof(TsListenerDouble.CallbackA));
            publisher.TsSubscribe(aliveListener, "Ev", nameof(TsListenerDouble.CallbackA));

            UnityEngine.Object.DestroyImmediate(listenerGo);

            Assert.DoesNotThrow(() => publisher.TsEmit("Ev"));
            Assert.AreEqual(1, aliveListener.CallbackACount);
        }

        [Test]
        public void TsDestroy_Default_DoesNotDestroySynchronously()
        {
            // Unity's Editor rejects UnityEngine.Object.Destroy() outside Play Mode,
            // logged as an Error the Test Runner otherwise treats as an unhandled
            // failure — expect it explicitly. Edit Mode can therefore only confirm the
            // GameObject survives the call, not that Destroy() defers to end-of-frame;
            // the deferred half of the contract needs Play Mode to observe.
            var behaviour = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            GameObject go = behaviour.gameObject;

            LogAssert.Expect(LogType.Error, new Regex("^Destroy may not be called from edit mode"));
            behaviour.TsDestroy();

            Assert.IsFalse(go == null, "The GameObject must not be destroyed synchronously by this call.");
        }

        [Test]
        public void TsDestroy_Override_ReplacesBaseBehaviourEntirely()
        {
            var behaviour = CreateBehaviour<TsvrcBehaviourNoDestroySubclass>();
            GameObject go = behaviour.gameObject;

            behaviour.TsDestroy();

            Assert.AreEqual(1, behaviour.TsDestroyCallCount);
            Assert.IsFalse(go == null, "Overridden TsDestroy must not fall back to destroying the object.");
        }

        [Test]
        public void TsDestroy_NeverConstructed_DoesNotThrow()
        {
            var behaviour = CreateBehaviour<TsvrcBehaviourTestSubclass>();

            LogAssert.Expect(LogType.Error, new Regex("^Destroy may not be called from edit mode"));
            Assert.DoesNotThrow(() => behaviour.TsDestroy());
        }

        [Test]
        public void TsStart_Base_IsEmptyNoOp()
        {
            var behaviour = CreateBehaviour<TsvrcBehaviour>();

            Assert.DoesNotThrow(() => behaviour.TsConstruct((TsRoot)null));
        }
    }
}
