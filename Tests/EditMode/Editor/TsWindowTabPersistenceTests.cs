using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Tsvrc.Editor;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // TsWindow builds its tab-module list once and never rebuilds it in ReloadConfig (see
    // TsWindow.ReloadTabs), so tab-only UI state like FactoryModule's foldouts survives a
    // regenerate. This verifies that survival across repeated ReloadConfig() calls.
    public class TsWindowTabPersistenceTests
    {
        private static readonly Type WindowType = typeof(TsWindow);
        private static readonly FieldInfo TabsField = WindowType.GetField("_tabs", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo ReloadTabsMethod = WindowType.GetMethod("ReloadTabs", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo ReloadConfigMethod = WindowType.GetMethod("ReloadConfig", BindingFlags.NonPublic | BindingFlags.Instance);

        private TsWindow _window;

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(TabsField, "TsWindow._tabs field changed or was removed.");
            Assert.IsNotNull(ReloadTabsMethod, "TsWindow.ReloadTabs method changed or was removed.");
            Assert.IsNotNull(ReloadConfigMethod, "TsWindow.ReloadConfig method changed or was removed.");

            _window = ScriptableObject.CreateInstance<TsWindow>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_window != null)
                UnityEngine.Object.DestroyImmediate(_window);
        }

        [Test]
        public void ReloadConfig_DoesNotRebuildTabList()
        {
            ReloadTabsMethod.Invoke(_window, null);
            var tabsBefore = (List<TsModule>)TabsField.GetValue(_window);

            // Simulate several TsGenerator.StateChanged events (e.g. one per keystroke).
            ReloadConfigMethod.Invoke(_window, null);
            ReloadConfigMethod.Invoke(_window, null);

            var tabsAfter = (List<TsModule>)TabsField.GetValue(_window);

            Assert.AreSame(tabsBefore, tabsAfter,
                "ReloadConfig() must not replace the tab list - module instances (e.g. FactoryModule) " +
                "hold their own tab-only UI state (foldouts) that must survive a regenerate.");
        }

        [Test]
        public void ReloadConfig_PreservesSameFactoryModuleInstance()
        {
            ReloadTabsMethod.Invoke(_window, null);
            var tabsBefore = (List<TsModule>)TabsField.GetValue(_window);
            var factoryModuleBefore = tabsBefore.FirstOrDefault(m => m.GetType().Name == "FactoryModule");
            Assert.IsNotNull(factoryModuleBefore, "Expected a FactoryModule tab to be present.");

            ReloadConfigMethod.Invoke(_window, null);

            var tabsAfter = (List<TsModule>)TabsField.GetValue(_window);
            var factoryModuleAfter = tabsAfter.FirstOrDefault(m => m.GetType().Name == "FactoryModule");

            Assert.AreSame(factoryModuleBefore, factoryModuleAfter,
                "The same FactoryModule instance must survive ReloadConfig(), or its per-group " +
                "foldout state (keyed by array index, stored on the instance) is silently lost.");
        }

        [Test]
        public void ReloadTabs_BuildsNonEmptyTabList()
        {
            ReloadTabsMethod.Invoke(_window, null);
            var tabs = (List<TsModule>)TabsField.GetValue(_window);

            Assert.IsNotNull(tabs);
            Assert.IsTrue(tabs.Count > 0, "Expected at least one tab-bearing module.");
        }
    }
}
