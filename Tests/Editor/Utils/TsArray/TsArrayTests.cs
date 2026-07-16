using NUnit.Framework;
using Tsvrc.Utils;
using UdonSharp;
using UnityEngine;

namespace Tsvrc.Tests.Editor
{
    public class TsArrayTests
    {
        [Test]
        public void Add_Strings_BothNonEmpty_ConcatenatesInOrder()
        {
            string[] result = TsArray.Add(new[] { "a", "b" }, new[] { "c", "d" });

            CollectionAssert.AreEqual(new[] { "a", "b", "c", "d" }, result);
        }

        [Test]
        public void Add_Strings_OriginalEmpty_ReturnsItemsOnly()
        {
            string[] result = TsArray.Add(new string[0], new[] { "c", "d" });

            CollectionAssert.AreEqual(new[] { "c", "d" }, result);
        }

        [Test]
        public void Add_Strings_ItemsEmpty_ReturnsOriginalOnly()
        {
            string[] result = TsArray.Add(new[] { "a", "b" }, new string[0]);

            CollectionAssert.AreEqual(new[] { "a", "b" }, result);
        }

        [Test]
        public void Add_Strings_BothEmpty_ReturnsEmptyArray()
        {
            string[] result = TsArray.Add(new string[0], new string[0]);

            Assert.AreEqual(0, result.Length);
        }

        [Test]
        public void Add_Strings_DoesNotMutateInputs()
        {
            string[] original = { "a" };
            string[] items = { "b" };

            TsArray.Add(original, items);

            CollectionAssert.AreEqual(new[] { "a" }, original);
            CollectionAssert.AreEqual(new[] { "b" }, items);
        }

        [Test]
        public void Add_Strings_NullOriginal_ThrowsNullReferenceException()
        {
            // Documents current behavior: none of TsArray's methods null-guard their
            // parameters, so a null array throws rather than being treated as empty.
            Assert.Throws<System.NullReferenceException>(() => TsArray.Add(null, new[] { "a" }));
        }

        [Test]
        public void Add_Strings_NullItems_ThrowsNullReferenceException()
        {
            Assert.Throws<System.NullReferenceException>(() => TsArray.Add(new[] { "a" }, null));
        }

        [Test]
        public void Remove_Strings_RemovesAllMatchingOccurrences_PreservesOrder()
        {
            string[] result = TsArray.Remove(new[] { "a", "b", "a", "c" }, new[] { "a" });

            CollectionAssert.AreEqual(new[] { "b", "c" }, result);
        }

        [Test]
        public void Remove_Strings_NoMatches_ReturnsEquivalentNewArray()
        {
            string[] original = { "a", "b" };
            string[] result = TsArray.Remove(original, new[] { "z" });

            CollectionAssert.AreEqual(new[] { "a", "b" }, result);
            Assert.AreNotSame(original, result, "Remove must always allocate a new array, even with zero removals.");
        }

        [Test]
        public void Remove_Strings_AllMatch_ReturnsEmptyArray()
        {
            string[] result = TsArray.Remove(new[] { "a", "a" }, new[] { "a" });

            Assert.AreEqual(0, result.Length);
        }

        [Test]
        public void Remove_Strings_ItemsEmpty_ReturnsAllOriginalElements()
        {
            string[] result = TsArray.Remove(new[] { "a", "b" }, new string[0]);

            CollectionAssert.AreEqual(new[] { "a", "b" }, result);
        }

        [Test]
        public void Remove_Strings_OriginalEmpty_ReturnsEmptyArray()
        {
            string[] result = TsArray.Remove(new string[0], new[] { "a" });

            Assert.AreEqual(0, result.Length);
        }

        [Test]
        public void Remove_Strings_MultipleItemsToRemove_RemovesAllOfThem()
        {
            string[] result = TsArray.Remove(new[] { "a", "b", "c", "d" }, new[] { "b", "d" });

            CollectionAssert.AreEqual(new[] { "a", "c" }, result);
        }

        [Test]
        public void Remove_Strings_DoesNotMutateInputs()
        {
            string[] original = { "a", "b" };
            string[] items = { "a" };

            TsArray.Remove(original, items);

            CollectionAssert.AreEqual(new[] { "a", "b" }, original);
            CollectionAssert.AreEqual(new[] { "a" }, items);
        }

        [Test]
        public void Remove_Strings_NullOriginal_ThrowsNullReferenceException()
        {
            Assert.Throws<System.NullReferenceException>(() => TsArray.Remove(null, new[] { "a" }));
        }

        [Test]
        public void Remove_Strings_NullItems_ThrowsNullReferenceException()
        {
            Assert.Throws<System.NullReferenceException>(() => TsArray.Remove(new[] { "a" }, null));
        }

        [Test]
        public void Contains_Strings_ValuePresent_ReturnsTrue()
        {
            Assert.IsTrue(TsArray.Contains(new[] { "a", "b" }, "b"));
        }

        [Test]
        public void Contains_Strings_ValueAbsent_ReturnsFalse()
        {
            Assert.IsFalse(TsArray.Contains(new[] { "a", "b" }, "z"));
        }

        [Test]
        public void Contains_Strings_EmptyArray_ReturnsFalse()
        {
            Assert.IsFalse(TsArray.Contains(new string[0], "a"));
        }

        [Test]
        public void Contains_Strings_NullArray_ThrowsNullReferenceException()
        {
            Assert.Throws<System.NullReferenceException>(() => TsArray.Contains(null, "a"));
        }

        [Test]
        public void Contains_Strings_NullValue_MatchesNullElement()
        {
            Assert.IsTrue(TsArray.Contains(new[] { "a", null }, null));
        }

        [Test]
        public void Dedupe_NoDuplicates_PreservesOrderAndReturnsNewArray()
        {
            string[] original = { "a", "b", "c" };
            string[] result = TsArray.Dedupe(original);

            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, result);
            Assert.AreNotSame(original, result, "Dedupe must always allocate a new array per its documented contract.");
        }

        [Test]
        public void Dedupe_WithDuplicates_CollapsesToFirstOccurrence_PreservesOrder()
        {
            string[] result = TsArray.Dedupe(new[] { "a", "b", "a", "c", "b", "a" });

            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, result);
        }

        [Test]
        public void Dedupe_EmptyArray_ReturnsNewEmptyArray()
        {
            string[] original = new string[0];
            string[] result = TsArray.Dedupe(original);

            Assert.AreEqual(0, result.Length);
            Assert.AreNotSame(original, result);
        }

        [Test]
        public void Dedupe_SingleElement_ReturnsNewArrayWithSameContent()
        {
            string[] original = { "a" };
            string[] result = TsArray.Dedupe(original);

            CollectionAssert.AreEqual(new[] { "a" }, result);
            Assert.AreNotSame(original, result, "Dedupe must copy even for length < 2, per its documented contract.");
        }

        [Test]
        public void Dedupe_MutatingResult_DoesNotAffectOriginal()
        {
            string[] original = { "a" };
            string[] result = TsArray.Dedupe(original);
            result[0] = "mutated";

            Assert.AreEqual("a", original[0]);
        }

        [Test]
        public void Dedupe_AllSameValue_ReturnsSingleElement()
        {
            string[] result = TsArray.Dedupe(new[] { "a", "a", "a" });

            CollectionAssert.AreEqual(new[] { "a" }, result);
        }

        [Test]
        public void Dedupe_NullArray_ThrowsNullReferenceException()
        {
            Assert.Throws<System.NullReferenceException>(() => TsArray.Dedupe(null));
        }

        [Test]
        public void Dedupe_ContainsNullElements_TreatsNullAsADedupableValue()
        {
            string[] result = TsArray.Dedupe(new[] { "a", null, "b", null });

            CollectionAssert.AreEqual(new[] { "a", null, "b" }, result);
        }

        [Test]
        public void Add_Behaviours_BothNonEmpty_ConcatenatesInOrder()
        {
            var go = new GameObject(nameof(TsArrayTests));
            try
            {
                var behaviour = go.AddComponent<TsArrayTestBehaviour>();
                UdonSharpBehaviour[] original = { behaviour };
                UdonSharpBehaviour[] items = { behaviour };

                UdonSharpBehaviour[] result = TsArray.Add(original, items);

                Assert.AreEqual(2, result.Length);
                Assert.AreSame(behaviour, result[0]);
                Assert.AreSame(behaviour, result[1]);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Add_Behaviours_BothEmpty_ReturnsEmptyArray()
        {
            UdonSharpBehaviour[] result = TsArray.Add(new UdonSharpBehaviour[0], new UdonSharpBehaviour[0]);

            Assert.AreEqual(0, result.Length);
        }

        [Test]
        public void Add_Behaviours_OriginalEmpty_ReturnsItemsOnly()
        {
            var go = new GameObject(nameof(TsArrayTests));
            try
            {
                var a = go.AddComponent<TsArrayTestBehaviour>();

                UdonSharpBehaviour[] result = TsArray.Add(new UdonSharpBehaviour[0], new UdonSharpBehaviour[] { a });

                Assert.AreEqual(1, result.Length);
                Assert.AreSame(a, result[0]);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Add_Behaviours_ItemsEmpty_ReturnsOriginalOnly()
        {
            var go = new GameObject(nameof(TsArrayTests));
            try
            {
                var a = go.AddComponent<TsArrayTestBehaviour>();

                UdonSharpBehaviour[] result = TsArray.Add(new UdonSharpBehaviour[] { a }, new UdonSharpBehaviour[0]);

                Assert.AreEqual(1, result.Length);
                Assert.AreSame(a, result[0]);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Add_Behaviours_DoesNotMutateInputs()
        {
            var go = new GameObject(nameof(TsArrayTests));
            try
            {
                var a = go.AddComponent<TsArrayTestBehaviour>();
                UdonSharpBehaviour[] original = { a };
                UdonSharpBehaviour[] items = new UdonSharpBehaviour[0];

                TsArray.Add(original, items);

                Assert.AreEqual(1, original.Length);
                Assert.AreSame(a, original[0]);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Add_Behaviours_NullOriginal_ThrowsNullReferenceException()
        {
            Assert.Throws<System.NullReferenceException>(() => TsArray.Add(null, new UdonSharpBehaviour[0]));
        }

        [Test]
        public void Add_Behaviours_NullItems_ThrowsNullReferenceException()
        {
            Assert.Throws<System.NullReferenceException>(() => TsArray.Add(new UdonSharpBehaviour[0], null));
        }

        [Test]
        public void Remove_Behaviours_RemovesMatchingReference_PreservesOrder()
        {
            var goA = new GameObject("A");
            var goB = new GameObject("B");
            try
            {
                var a = goA.AddComponent<TsArrayTestBehaviour>();
                var b = goB.AddComponent<TsArrayTestBehaviour>();
                UdonSharpBehaviour[] original = { a, b, a };

                UdonSharpBehaviour[] result = TsArray.Remove(original, new UdonSharpBehaviour[] { a });

                Assert.AreEqual(1, result.Length);
                Assert.AreSame(b, result[0]);
            }
            finally
            {
                Object.DestroyImmediate(goA);
                Object.DestroyImmediate(goB);
            }
        }

        [Test]
        public void Remove_Behaviours_NoMatches_ReturnsNewArrayWithSameElements()
        {
            var go = new GameObject(nameof(TsArrayTests));
            try
            {
                var a = go.AddComponent<TsArrayTestBehaviour>();
                UdonSharpBehaviour[] original = { a };

                UdonSharpBehaviour[] result = TsArray.Remove(original, new UdonSharpBehaviour[0]);

                Assert.AreEqual(1, result.Length);
                Assert.AreSame(a, result[0]);
                Assert.AreNotSame(original, result);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Remove_Behaviours_OriginalEmpty_ReturnsEmptyArray()
        {
            UdonSharpBehaviour[] result = TsArray.Remove(new UdonSharpBehaviour[0], new UdonSharpBehaviour[0]);

            Assert.AreEqual(0, result.Length);
        }

        [Test]
        public void Remove_Behaviours_AllMatch_ReturnsEmptyArray()
        {
            var go = new GameObject(nameof(TsArrayTests));
            try
            {
                var a = go.AddComponent<TsArrayTestBehaviour>();
                UdonSharpBehaviour[] original = { a, a };

                UdonSharpBehaviour[] result = TsArray.Remove(original, new UdonSharpBehaviour[] { a });

                Assert.AreEqual(0, result.Length);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Remove_Behaviours_MultipleItemsToRemove_RemovesAllOfThem()
        {
            var goA = new GameObject("A");
            var goB = new GameObject("B");
            var goC = new GameObject("C");
            var goD = new GameObject("D");
            try
            {
                var a = goA.AddComponent<TsArrayTestBehaviour>();
                var b = goB.AddComponent<TsArrayTestBehaviour>();
                var c = goC.AddComponent<TsArrayTestBehaviour>();
                var d = goD.AddComponent<TsArrayTestBehaviour>();
                UdonSharpBehaviour[] original = { a, b, c, d };

                UdonSharpBehaviour[] result = TsArray.Remove(original, new UdonSharpBehaviour[] { b, d });

                Assert.AreEqual(2, result.Length);
                Assert.AreSame(a, result[0]);
                Assert.AreSame(c, result[1]);
            }
            finally
            {
                Object.DestroyImmediate(goA);
                Object.DestroyImmediate(goB);
                Object.DestroyImmediate(goC);
                Object.DestroyImmediate(goD);
            }
        }

        [Test]
        public void Remove_Behaviours_DoesNotMutateInputs()
        {
            var goA = new GameObject("A");
            var goB = new GameObject("B");
            try
            {
                var a = goA.AddComponent<TsArrayTestBehaviour>();
                var b = goB.AddComponent<TsArrayTestBehaviour>();
                UdonSharpBehaviour[] original = { a, b };
                UdonSharpBehaviour[] items = { a };

                TsArray.Remove(original, items);

                Assert.AreEqual(2, original.Length);
                Assert.AreSame(a, original[0]);
                Assert.AreSame(b, original[1]);
                Assert.AreEqual(1, items.Length);
                Assert.AreSame(a, items[0]);
            }
            finally
            {
                Object.DestroyImmediate(goA);
                Object.DestroyImmediate(goB);
            }
        }

        [Test]
        public void Remove_Behaviours_NullOriginal_ThrowsNullReferenceException()
        {
            Assert.Throws<System.NullReferenceException>(() => TsArray.Remove(null, new UdonSharpBehaviour[0]));
        }

        [Test]
        public void Remove_Behaviours_NullItems_ThrowsNullReferenceException()
        {
            Assert.Throws<System.NullReferenceException>(() => TsArray.Remove(new UdonSharpBehaviour[0], null));
        }

        [Test]
        public void Contains_Behaviours_ReferencePresent_ReturnsTrue()
        {
            var go = new GameObject(nameof(TsArrayTests));
            try
            {
                var a = go.AddComponent<TsArrayTestBehaviour>();

                Assert.IsTrue(TsArray.Contains(new UdonSharpBehaviour[] { a }, a));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Contains_Behaviours_ReferenceAbsent_ReturnsFalse()
        {
            var goA = new GameObject("A");
            var goB = new GameObject("B");
            try
            {
                var a = goA.AddComponent<TsArrayTestBehaviour>();
                var b = goB.AddComponent<TsArrayTestBehaviour>();

                Assert.IsFalse(TsArray.Contains(new UdonSharpBehaviour[] { a }, b));
            }
            finally
            {
                Object.DestroyImmediate(goA);
                Object.DestroyImmediate(goB);
            }
        }

        [Test]
        public void Contains_Behaviours_EmptyArray_ReturnsFalse()
        {
            var go = new GameObject(nameof(TsArrayTests));
            try
            {
                var a = go.AddComponent<TsArrayTestBehaviour>();

                Assert.IsFalse(TsArray.Contains(new UdonSharpBehaviour[0], a));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Contains_Behaviours_NullArray_ThrowsNullReferenceException()
        {
            var go = new GameObject(nameof(TsArrayTests));
            try
            {
                var a = go.AddComponent<TsArrayTestBehaviour>();

                Assert.Throws<System.NullReferenceException>(() => TsArray.Contains(null, a));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Contains_Behaviours_NullValue_MatchesLiteralNullElement()
        {
            Assert.IsTrue(TsArray.Contains(new UdonSharpBehaviour[] { null }, null));
        }

    }
}
