using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Editor;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // ObjectListGUI.IsDuplicate - internal static, called directly. Mirrors
    // ObjectListGUIIsSceneInstanceTests.cs for the sibling assetsOnly check.
    public class ObjectListGUIIsDuplicateTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void IsDuplicate_Null_ReturnsFalseAndDoesNotAddToSeen()
        {
            var seen = new HashSet<Object>();

            Assert.IsFalse(ObjectListGUI.IsDuplicate(null, seen));
            Assert.IsEmpty(seen);
        }

        [Test]
        public void IsDuplicate_FirstOccurrence_ReturnsFalseAndAddsToSeen()
        {
            _go = new GameObject("First");
            var seen = new HashSet<Object>();

            Assert.IsFalse(ObjectListGUI.IsDuplicate(_go, seen));
            CollectionAssert.Contains(seen, _go);
        }

        [Test]
        public void IsDuplicate_SecondOccurrenceOfSameObject_ReturnsTrue()
        {
            _go = new GameObject("Repeated");
            var seen = new HashSet<Object>();
            ObjectListGUI.IsDuplicate(_go, seen); // first occurrence

            Assert.IsTrue(ObjectListGUI.IsDuplicate(_go, seen));
        }

        [Test]
        public void IsDuplicate_DifferentObjects_NeitherIsFlagged()
        {
            var a = new GameObject("A");
            var b = new GameObject("B");
            _go = a; // TearDown cleanup for one; clean up b explicitly below
            var seen = new HashSet<Object>();

            try
            {
                Assert.IsFalse(ObjectListGUI.IsDuplicate(a, seen));
                Assert.IsFalse(ObjectListGUI.IsDuplicate(b, seen));
            }
            finally
            {
                Object.DestroyImmediate(b);
            }
        }
    }
}
