using System.Reflection;
using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Tsvrc.Tests.EditMode
{
    // Verifies OpenLinkedScene() refreshes _config as soon as the linked scene loads, without
    // relying on a window focus change to reach OnFocus's ReloadConfig().
    public class TsWindowOpenLinkedSceneTests
    {
        private static readonly MethodInfo OpenLinkedSceneMethod =
            typeof(TsWindow).GetMethod("OpenLinkedScene", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo ConfigField =
            typeof(TsWindow).GetField("_config", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo SerializedObjectField =
            typeof(TsWindow).GetField("_so", BindingFlags.NonPublic | BindingFlags.Instance);

        private TsGeneratorTestHarness _harness;
        private TsWindow _window;

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(OpenLinkedSceneMethod, "TsWindow.OpenLinkedScene method changed or was removed.");
            Assert.IsNotNull(ConfigField, "TsWindow._config field changed or was removed.");
            Assert.IsNotNull(SerializedObjectField, "TsWindow._so field changed or was removed.");

            _harness = new TsGeneratorTestHarness();
            _window = ScriptableObject.CreateInstance<TsWindow>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_window != null) Object.DestroyImmediate(_window);
            _harness.Dispose();
        }

        // Saves the harness's temp scene (with a TsConfig on it) to a real scratch path, links
        // it, then swaps the active scene away so it's genuinely unloaded - the exact
        // precondition "Open Linked Scene" is offered for.
        private string LinkRealSceneButLeaveItUnloaded()
        {
            _harness.CreateGameObject("Config").AddComponent<TsConfig>();
            string scenePath = ScratchAssets.Folder + "/OpenLinkedSceneTest.unity";
            EditorSceneManager.SaveScene(_harness.Scope.Scene, scenePath);

            TsLinkedScene.ClearOverride(); // this test cares about the real linked path, not the scope's own override
            TsLinkedScene.ScenePath = scenePath;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // Saved (rather than left as a dirty untitled scene) purely so OpenLinkedScene()'s
            // SaveCurrentModifiedScenesIfUserWantsTo() call below finds nothing dirty and returns
            // immediately instead of popping a save-changes dialog - this scene is just standing
            // in for "some other scene is open right now".
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScratchAssets.Folder + "/OpenLinkedSceneTestOther.unity");

            return scenePath;
        }

        [Test]
        public void OpenLinkedScene_LoadsTheSceneAndRefreshesConfigWithoutNeedingAFocusChange()
        {
            LinkRealSceneButLeaveItUnloaded();
            Assert.IsTrue(TsLinkedScene.IsConfiguredButNotLoaded, "Precondition for this test.");
            Assert.IsNull(ConfigField.GetValue(_window), "Precondition: window hasn't resolved a config yet.");

            OpenLinkedSceneMethod.Invoke(_window, null);

            Assert.IsFalse(TsLinkedScene.IsConfiguredButNotLoaded, "The linked scene should now be loaded.");
            Assert.IsNotNull(ConfigField.GetValue(_window),
                "OpenLinkedScene() must refresh _config itself - OnFocus's ReloadConfig() only runs on a " +
                "focus change, which a same-window button click never triggers.");
            Assert.IsNotNull(SerializedObjectField.GetValue(_window),
                "The SerializedObject backing the tabs must be rebuilt against the newly found config too.");
        }
    }
}
