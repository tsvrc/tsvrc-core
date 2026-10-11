using NUnit.Framework;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System;
using Tsvrc.Core.Generated;
using Tsvrc.Core;
using Tsvrc.UdonTestKit;
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
            using var calls = new CallLog();
            var publisher = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            CallbackSpy listener = calls.CreateSpy("Listener");

            publisher.TsSubscribe(listener, "Ev", nameof(CallbackSpy.CallbackA));

            var listeners = PrivateFieldAccess.GetField<UdonSharp.UdonSharpBehaviour[]>(publisher, "_subListeners");
            var keys = PrivateFieldAccess.GetField<string[]>(publisher, "_subKeys");
            var callbacks = PrivateFieldAccess.GetField<string[]>(publisher, "_subCallbacks");

            // The backing arrays are pre-grown capacity, not exact-size, so only
            // _subCount and the populated slots are part of the contract; array Length
            // is an implementation detail.
            Assert.AreEqual(1, PrivateFieldAccess.GetField<int>(publisher, "_subCount"));
            Assert.AreSame(listener, listeners[0]);
            Assert.AreEqual("Ev", keys[0]);
            Assert.AreEqual(nameof(CallbackSpy.CallbackA), callbacks[0]);
        }

        [Test]
        public void TsSubscribe_SameListenerTwiceForSameEvent_DeliversCallbackTwice()
        {
            using var calls = new CallLog();
            var publisher = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            CallbackSpy listener = calls.CreateSpy("Listener");

            publisher.TsSubscribe(listener, "Ev", nameof(CallbackSpy.CallbackA));
            publisher.TsSubscribe(listener, "Ev", nameof(CallbackSpy.CallbackA));
            publisher.TsEmit("Ev");

            CollectionAssert.AreEqual(new[] { "Listener.CallbackA", "Listener.CallbackA" }, calls);
        }

        [Test]
        public void TsSubscribe_SameListenerDifferentEvents_BothRecorded()
        {
            using var calls = new CallLog();
            var publisher = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            CallbackSpy listener = calls.CreateSpy("Listener");

            publisher.TsSubscribe(listener, "EvA", nameof(CallbackSpy.CallbackA));
            publisher.TsSubscribe(listener, "EvB", nameof(CallbackSpy.CallbackB));
            publisher.TsEmit("EvA");
            publisher.TsEmit("EvB");

            CollectionAssert.AreEqual(new[] { "Listener.CallbackA", "Listener.CallbackB" }, calls);
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
            using var calls = new CallLog();
            var publisher = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            CallbackSpy listenerForNull = calls.CreateSpy("ListenerNull");
            CallbackSpy listenerForEmpty = calls.CreateSpy("ListenerEmpty");

            publisher.TsSubscribe(listenerForNull, null, nameof(CallbackSpy.CallbackA));
            publisher.TsSubscribe(listenerForEmpty, "", nameof(CallbackSpy.CallbackA));

            publisher.TsEmit(null);
            publisher.TsEmit("");

            CollectionAssert.AreEqual(new[] { "ListenerNull.CallbackA", "ListenerEmpty.CallbackA" }, calls);
        }

        [Test]
        public void TsSubscribe_GrowsCapacityByDoublingInsteadOfReallocatingEveryCall()
        {
            using var calls = new CallLog();
            var publisher = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            CallbackSpy listenerA = calls.CreateSpy("A");
            CallbackSpy listenerB = calls.CreateSpy("B");
            CallbackSpy filler = calls.CreateSpy("Filler");
            CallbackSpy overflow = calls.CreateSpy("Overflow");

            publisher.TsSubscribe(listenerA, "Ev", nameof(CallbackSpy.CallbackA));
            var afterFirst = PrivateFieldAccess.GetField<UdonSharp.UdonSharpBehaviour[]>(publisher, "_subListeners");

            // Subscribing again while capacity remains must reuse the same array.
            publisher.TsSubscribe(listenerB, "Ev", nameof(CallbackSpy.CallbackA));
            var afterSecond = PrivateFieldAccess.GetField<UdonSharp.UdonSharpBehaviour[]>(publisher, "_subListeners");
            Assert.AreSame(afterFirst, afterSecond, "Subscribing within existing capacity must not reallocate.");

            // Fill remaining capacity, then subscribe once more to force a grow.
            while (PrivateFieldAccess.GetField<int>(publisher, "_subCount") < afterSecond.Length)
                publisher.TsSubscribe(filler, "Filler", nameof(CallbackSpy.CallbackA));
            var full = PrivateFieldAccess.GetField<UdonSharp.UdonSharpBehaviour[]>(publisher, "_subListeners");

            publisher.TsSubscribe(overflow, "Ev", nameof(CallbackSpy.CallbackA));
            var afterGrow = PrivateFieldAccess.GetField<UdonSharp.UdonSharpBehaviour[]>(publisher, "_subListeners");

            Assert.AreNotSame(full, afterGrow, "Subscribing beyond capacity must reallocate.");
            Assert.Greater(afterGrow.Length, full.Length);
        }

        [Test]
        public void TsSubscribe_CapacityGrowth_PreservesAlreadySubscribedEntries()
        {
            using var calls = new CallLog();
            var publisher = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            var expected = new string[10];
            for (int i = 0; i < expected.Length; i++)
            {
                publisher.TsSubscribe(calls.CreateSpy("L" + i), "Ev", nameof(CallbackSpy.CallbackA));
                expected[i] = "L" + i + ".CallbackA";
            }

            publisher.TsEmit("Ev");

            CollectionAssert.AreEqual(expected, calls);
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
            using var calls = new CallLog();
            var publisher = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            publisher.TsSubscribe(calls.CreateSpy("Listener"), "Ev", nameof(CallbackSpy.CallbackA));

            publisher.TsEmit("Ev");

            CollectionAssert.AreEqual(new[] { "Listener.CallbackA" }, calls);
        }

        [Test]
        public void TsEmit_MultipleSubscribers_AllInvoked()
        {
            using var calls = new CallLog();
            var publisher = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            publisher.TsSubscribe(calls.CreateSpy("A"), "Ev", nameof(CallbackSpy.CallbackA));
            publisher.TsSubscribe(calls.CreateSpy("B"), "Ev", nameof(CallbackSpy.CallbackA));

            publisher.TsEmit("Ev");

            CollectionAssert.AreEquivalent(new[] { "A.CallbackA", "B.CallbackA" }, calls);
        }

        [Test]
        public void TsEmit_DifferentEvent_DoesNotCrossInvoke()
        {
            using var calls = new CallLog();
            var publisher = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            publisher.TsSubscribe(calls.CreateSpy("Listener"), "EventY", nameof(CallbackSpy.CallbackA));

            publisher.TsEmit("EventX");

            CollectionAssert.IsEmpty(calls);
        }

        [Test]
        public void TsEmit_DeliveryOrder_MatchesSubscriptionOrder()
        {
            using var calls = new CallLog();
            var publisher = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            publisher.TsSubscribe(calls.CreateSpy("First"), "Ev", nameof(CallbackSpy.CallbackA));
            publisher.TsSubscribe(calls.CreateSpy("Second"), "Ev", nameof(CallbackSpy.CallbackA));

            publisher.TsEmit("Ev");

            CollectionAssert.AreEqual(new[] { "First.CallbackA", "Second.CallbackA" }, calls);
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
            using var calls = new CallLog();
            var publisher = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            publisher.TsSubscribe(calls.CreateSpy("Listener"), "Ev", nameof(CallbackSpy.CallbackA));
            publisher.TsSubscribe(null, "Ev", "Callback");

            Assert.Throws<NullReferenceException>(() => publisher.TsEmit("Ev"));
            CollectionAssert.AreEqual(new[] { "Listener.CallbackA" }, calls);
        }

        [Test]
        public void TsEmit_ReentrantSubscribeDuringEmit_NewSubscriberNotInvokedThisEmit()
        {
            // TsEmit snapshots the arrays before iterating, and TsSubscribe never
            // mutates in place, so a subscription added mid-emit is not invoked until
            // the next TsEmit call.
            using var calls = new CallLog();
            var publisher = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            var reentrant = calls.CreateSpy<ReentrantSubscriberSpy>("Reentrant");
            reentrant.Publisher = publisher;
            reentrant.EventName = "Ev";
            reentrant.NewListener = calls.CreateSpy("New");
            publisher.TsSubscribe(reentrant, "Ev", nameof(ReentrantSubscriberSpy.SubscribeNewListener));

            publisher.TsEmit("Ev");

            CollectionAssert.AreEqual(new[] { "Reentrant.SubscribeNewListener" }, calls);

            calls.Clear();
            publisher.TsEmit("Ev");

            CollectionAssert.AreEqual(new[] { "Reentrant.SubscribeNewListener", "New.CallbackA" }, calls);
        }

        [Test]
        public void TsEmit_UnconstructedBehaviour_StillWorks()
        {
            using var calls = new CallLog();
            var publisher = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            publisher.TsSubscribe(calls.CreateSpy("Listener"), "Ev", nameof(CallbackSpy.CallbackA));

            Assert.DoesNotThrow(() => publisher.TsEmit("Ev"));
            CollectionAssert.AreEqual(new[] { "Listener.CallbackA" }, calls);
        }

        [Test]
        public void TsEmit_ListenerGameObjectDestroyedBeforeEmit_IsSkippedSilently()
        {
            // A listener destroyed after subscribing is skipped instead of receiving a
            // callback on a dead object, unlike a listener that was null at subscribe time,
            // which still throws (see TsEmit_ListenerNullAtSubscribeTime_
            // ThrowsNullReferenceException above).
            using var calls = new CallLog();
            var publisher = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            CallbackSpy listener = calls.CreateSpy("Destroyed");
            publisher.TsSubscribe(listener, "Ev", nameof(CallbackSpy.CallbackA));

            UnityEngine.Object.DestroyImmediate(listener.gameObject);

            Assert.DoesNotThrow(() => publisher.TsEmit("Ev"));
            CollectionAssert.IsEmpty(calls);
        }

        [Test]
        public void TsEmit_DestroyedListenerAmongOthers_OthersStillReceiveCallback()
        {
            using var calls = new CallLog();
            var publisher = CreateBehaviour<TsvrcBehaviourTestSubclass>();
            CallbackSpy destroyedListener = calls.CreateSpy("Destroyed");
            publisher.TsSubscribe(destroyedListener, "Ev", nameof(CallbackSpy.CallbackA));
            publisher.TsSubscribe(calls.CreateSpy("Alive"), "Ev", nameof(CallbackSpy.CallbackA));

            UnityEngine.Object.DestroyImmediate(destroyedListener.gameObject);

            Assert.DoesNotThrow(() => publisher.TsEmit("Ev"));
            CollectionAssert.AreEqual(new[] { "Alive.CallbackA" }, calls);
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
