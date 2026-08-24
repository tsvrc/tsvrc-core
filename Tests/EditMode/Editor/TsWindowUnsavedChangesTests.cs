using System;
using System.Reflection;
using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // TsWindow's Apply/Discard wiring: hasUnsavedChanges tracks _pending.HasPendingChanges,
    // SaveChanges/DiscardChanges call through to TsPendingConfigEdit, and _persistedTabIndex
    // survives a simulated OnEnable re-run (domain reload). Reflection-driven since TsWindow's
    // relevant fields/methods are private - mirrors TsWindowTabPersistenceTests' own pattern.
    public class TsWindowUnsavedChangesTests
    {
        private static readonly Type WindowType = typeof(TsWindow);
        private static readonly FieldInfo PendingField = WindowType.GetField("_pending", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo SoField = WindowType.GetField("_so", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo PersistedTabIndexField = WindowType.GetField("_persistedTabIndex", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo TabIndexField = WindowType.GetField("_tabIndex", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo ReloadTabsMethod = WindowType.GetMethod("ReloadTabs", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo ReloadConfigMethod = WindowType.GetMethod("ReloadConfig", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo OnEnableMethod = WindowType.GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);

        private TempSceneScope _scope;
        private TsConfig _config;
        private TsWindow _window;

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(PendingField, "TsWindow._pending field changed or was removed.");
            Assert.IsNotNull(SoField, "TsWindow._so field changed or was removed.");
            Assert.IsNotNull(PersistedTabIndexField, "TsWindow._persistedTabIndex field changed or was removed.");
            Assert.IsNotNull(TabIndexField, "TsWindow._tabIndex field changed or was removed.");
            Assert.IsNotNull(ReloadTabsMethod, "TsWindow.ReloadTabs method changed or was removed.");
            Assert.IsNotNull(ReloadConfigMethod, "TsWindow.ReloadConfig method changed or was removed.");
            Assert.IsNotNull(OnEnableMethod, "TsWindow.OnEnable method changed or was removed.");

            _scope = new TempSceneScope();
            _config = _scope.CreateGameObject("Config").AddComponent<TsConfig>();
            _window = ScriptableObject.CreateInstance<TsWindow>();
            ReloadTabsMethod.Invoke(_window, null);
            ReloadConfigMethod.Invoke(_window, null);
        }

        [TearDown]
        public void TearDown()
        {
            if (_window != null)
                UnityEngine.Object.DestroyImmediate(_window);
            _scope.Dispose();
        }

        private TsPendingConfigEdit Pending => (TsPendingConfigEdit)PendingField.GetValue(_window);
        private SerializedObject So => (SerializedObject)SoField.GetValue(_window);

        [Test]
        public void ReloadConfig_NoEdit_HasUnsavedChangesIsFalse()
        {
            Assert.IsFalse(_window.hasUnsavedChanges);
        }

        private void ApplyRealEdit()
        {
            var so = So;
            so.Update();
            so.FindProperty(nameof(TsConfig.TreeShakeUnused)).boolValue = true;
            bool applied = so.ApplyModifiedProperties();
            Pending.NotifyAppliedToSerializedObject(applied);
        }

        [Test]
        public void SaveChanges_AppliesPendingEditAndClearsHasUnsavedChanges()
        {
            ApplyRealEdit();
            Assert.IsTrue(Pending.HasPendingChanges, "Precondition: an edit must be pending.");

            _window.SaveChanges();

            Assert.IsFalse(_window.hasUnsavedChanges);
            Assert.IsTrue(_config.TreeShakeUnused, "SaveChanges must commit the edit, not revert it.");
        }

        [Test]
        public void DiscardChanges_RevertsPendingEditAndClearsHasUnsavedChanges()
        {
            ApplyRealEdit();
            Assert.IsTrue(Pending.HasPendingChanges, "Precondition: an edit must be pending.");

            _window.DiscardChanges();

            Assert.IsFalse(_window.hasUnsavedChanges);
            Assert.IsFalse(_config.TreeShakeUnused, "DiscardChanges must revert the edit.");
        }

        [Test]
        public void PersistedTabIndex_SurvivesSimulatedDomainReload()
        {
            TabIndexField.SetValue(_window, 1);
            PersistedTabIndexField.SetValue(_window, 1);

            // OnEnable is what fires again after a real domain reload (the window instance
            // itself survives, plain fields do not - only [SerializeField] ones do).
            TabIndexField.SetValue(_window, 0);
            OnEnableMethod.Invoke(_window, null);

            Assert.AreEqual(1, (int)TabIndexField.GetValue(_window),
                "_tabIndex must be restored from the [SerializeField] _persistedTabIndex in OnEnable.");
        }

        [Test]
        public void PersistedTabIndex_OutOfRangeAfterTabsShrink_IsIgnored()
        {
            PersistedTabIndexField.SetValue(_window, 9999);

            Assert.DoesNotThrow(() => OnEnableMethod.Invoke(_window, null));
        }
    }
}
