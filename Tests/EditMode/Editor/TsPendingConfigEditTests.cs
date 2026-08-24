using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // TsPendingConfigEdit: the Apply/Discard batching mechanism shared by TsWindow and
    // TsBuiltinConfigInspector. Exercised against a real TsConfig component (via TempSceneScope),
    // since Discard's correctness depends on real SerializedObject/Object-reference round-tripping
    // that a fake target couldn't exercise honestly.
    public class TsPendingConfigEditTests
    {
        private TempSceneScope _scope;
        private TsConfig _config;
        private SerializedObject _so;

        [SetUp]
        public void SetUp()
        {
            _scope = new TempSceneScope();
            _config = _scope.CreateGameObject("Config").AddComponent<TsConfig>();
            _so = new SerializedObject(_config);
        }

        [TearDown]
        public void TearDown() => _scope.Dispose();

        [Test]
        public void HasChanged_SameJson_ReturnsFalse()
        {
            Assert.IsFalse(TsPendingConfigEdit.HasChanged("{\"a\":1}", "{\"a\":1}"));
        }

        [Test]
        public void HasChanged_DifferentJson_ReturnsTrue()
        {
            Assert.IsTrue(TsPendingConfigEdit.HasChanged("{\"a\":1}", "{\"a\":2}"));
        }

        [Test]
        public void HasChanged_NullBaseline_TreatsAnyJsonAsChanged()
        {
            Assert.IsTrue(TsPendingConfigEdit.HasChanged(null, "{\"a\":1}"));
        }

        [Test]
        public void BeginTracking_NoEditYet_HasNoPendingChanges()
        {
            var pending = new TsPendingConfigEdit();

            pending.BeginTracking(_config);

            Assert.IsFalse(pending.HasPendingChanges);
        }

        [Test]
        public void NotifyAppliedToSerializedObject_RealEdit_MarksPending()
        {
            var pending = new TsPendingConfigEdit();
            pending.BeginTracking(_config);

            _so.Update();
            _so.FindProperty(nameof(TsConfig.TreeShakeUnused)).boolValue = true;
            bool applied = _so.ApplyModifiedProperties();
            pending.NotifyAppliedToSerializedObject(applied);

            Assert.IsTrue(pending.HasPendingChanges);
        }

        [Test]
        public void NotifyAppliedToSerializedObject_NoEdit_StaysNotPending()
        {
            var pending = new TsPendingConfigEdit();
            pending.BeginTracking(_config);

            pending.NotifyAppliedToSerializedObject(anyChangesApplied: false);

            Assert.IsFalse(pending.HasPendingChanges);
        }

        [Test]
        public void Apply_ClearsPendingAndRebaselines()
        {
            var pending = new TsPendingConfigEdit();
            pending.BeginTracking(_config);
            _so.Update();
            _so.FindProperty(nameof(TsConfig.TreeShakeUnused)).boolValue = true;
            pending.NotifyAppliedToSerializedObject(_so.ApplyModifiedProperties());
            Assert.IsTrue(pending.HasPendingChanges, "Precondition: an edit must be pending before Apply.");

            pending.Apply();

            Assert.IsFalse(pending.HasPendingChanges);
            Assert.IsTrue(_config.TreeShakeUnused, "Apply must not revert the edit - it commits it.");
        }

        [Test]
        public void Discard_RevertsScalarEdit()
        {
            var pending = new TsPendingConfigEdit();
            pending.BeginTracking(_config);
            _so.Update();
            _so.FindProperty(nameof(TsConfig.TreeShakeUnused)).boolValue = true;
            pending.NotifyAppliedToSerializedObject(_so.ApplyModifiedProperties());

            pending.Discard(_so);

            Assert.IsFalse(pending.HasPendingChanges);
            Assert.IsFalse(_config.TreeShakeUnused, "Discard must revert to the state at BeginTracking.");
        }

        [Test]
        public void Discard_RevertsGroupedEntryArray_IncludingObjectReference()
        {
            // The bug this guards against: EditorJsonUtility.FromJsonOverwrite does not reliably
            // restore UnityEngine.Object references, so Discard uses EditorUtility.CopySerialized
            // against a hidden baseline clone instead - this test would fail (Value coming back
            // null) if Discard were ever changed back to a JSON-based revert.
            var marker = _scope.CreateGameObject("Marker");
            var pending = new TsPendingConfigEdit();
            pending.BeginTracking(_config);

            _so.Update();
            var entriesProp = _so.FindProperty(nameof(TsConfig.GlobalEntries));
            entriesProp.InsertArrayElementAtIndex(0);
            entriesProp.GetArrayElementAtIndex(0).FindPropertyRelative("Value").objectReferenceValue = marker;
            pending.NotifyAppliedToSerializedObject(_so.ApplyModifiedProperties());
            Assert.AreEqual(1, _config.GlobalEntries.Length, "Precondition: the insert must have applied.");

            pending.Discard(_so);

            Assert.AreEqual(0, _config.GlobalEntries.Length, "Discard must remove the array element added after baseline.");
        }

        [Test]
        public void Cleanup_ThenBeginTracking_StillWorks()
        {
            var pending = new TsPendingConfigEdit();
            pending.BeginTracking(_config);

            pending.Cleanup();
            pending.BeginTracking(_config);

            Assert.IsFalse(pending.HasPendingChanges);
        }

        [Test]
        public void BeginTracking_ScriptableObjectTarget_DoesNotThrow()
        {
            var asset = ScriptableObject.CreateInstance<TsBuiltinConfig>();
            var pending = new TsPendingConfigEdit();

            try
            {
                Assert.DoesNotThrow(() => pending.BeginTracking(asset));
                pending.Cleanup();
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void BeginTracking_SameTargetTwice_DoesNotRebaselineOrLosePendingState()
        {
            var pending = new TsPendingConfigEdit();
            pending.BeginTracking(_config);
            _so.Update();
            _so.FindProperty(nameof(TsConfig.TreeShakeUnused)).boolValue = true;
            pending.NotifyAppliedToSerializedObject(_so.ApplyModifiedProperties());

            pending.BeginTracking(_config);

            Assert.IsTrue(pending.HasPendingChanges,
                "Re-tracking the same target (e.g. every ReloadConfig() call) must not silently rebaseline " +
                "against the current, already-edited state and lose the pending flag.");
        }
    }
}
