using System;
using NUnit.Framework;
using Tsvrc.Editor;
using Tsvrc.StateMachine;
using UdonSharp;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Tests.Editor
{
    // InstanceModule.Wire() against a real compiled root. Creating a real child+component
    // (CreateComponent) needs `_detectedType` to be a genuine, already-compiled
    // UdonSharpBehaviour with a real script asset - using an actual production
    // TsInstance subclass would work, but this project intentionally has none yet, and
    // adding one as a permanent test double would change TsGenerator.HasBootstrapSignal()
    // for the whole project, not just tests (InstanceModule doesn't care whether the
    // "detected" type is actually a TsInstance subclass for the plumbing exercised here,
    // only DetectInstanceType() does - which isn't under test in this file). So
    // `StateManager` stands in as a real, already-compiled UdonSharpBehaviour:
    // CreateComponent()'s mechanics (script lookup, program asset creation, component add)
    // don't care that it isn't a TsInstance. Its transient
    // "Assets/TsGenerated/StateManager.asset" program asset is backed up/restored like
    // every other real-file side effect in this suite.
    public class InstanceModuleWireTests
    {
        private const string StateManagerAssetPath = "Assets/TsGenerated/StateManager.asset";

        private TempSceneScope _scope;
        private Component _root;
        private bool _assetExistedBefore;
        private byte[] _assetBackup;
        private byte[] _assetMetaBackup;

        [SetUp]
        public void SetUp()
        {
            _scope = new TempSceneScope();
            _root = CompiledRootFixture.AddTo(_scope);

            string fullPath = ToFullPath(StateManagerAssetPath);
            string metaPath = fullPath + ".meta";
            _assetExistedBefore = System.IO.File.Exists(fullPath);
            if (_assetExistedBefore)
            {
                _assetBackup = System.IO.File.ReadAllBytes(fullPath);
                _assetMetaBackup = System.IO.File.Exists(metaPath) ? System.IO.File.ReadAllBytes(metaPath) : null;
            }
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();

            string fullPath = ToFullPath(StateManagerAssetPath);
            string metaPath = fullPath + ".meta";
            if (_assetExistedBefore)
            {
                System.IO.File.WriteAllBytes(fullPath, _assetBackup);
                if (_assetMetaBackup != null) System.IO.File.WriteAllBytes(metaPath, _assetMetaBackup);
            }
            else
            {
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(StateManagerAssetPath) != null)
                    AssetDatabase.DeleteAsset(StateManagerAssetPath);
            }
            AssetDatabase.Refresh();
        }

        private static string ToFullPath(string assetPath)
        {
            string projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
            return System.IO.Path.Combine(projectRoot, assetPath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        }

        private static InstanceModule ModuleWith(Type detectedType, bool ambiguous)
        {
            var module = new InstanceModule();
            PrivateFieldAccess.SetField(module, "_detectedType", detectedType);
            PrivateFieldAccess.SetField(module, "_ambiguous", ambiguous);
            return module;
        }

        private UnityEngine.Object InstanceFieldValue()
            => new SerializedObject(_root).FindProperty("_instance").objectReferenceValue;

        // Note: `_instance` is declared as `TsInstance`, and `StateManager` (the real,
        // already-compiled stand-in type used throughout this file - see the class comment)
        // does NOT extend TsInstance. Unity's SerializedProperty.objectReferenceValue
        // setter silently clamps an incompatible-type assignment to null rather than
        // throwing, so the *field value* can't be faithfully asserted against a StateManager
        // instance here - only the scene-structure half (child/component creation) can be.
        // Confirming the field-assignment *mechanism* itself (a compatible-type value really
        // does get assigned) is exactly what MemoryModuleWireTests/SingletonModuleWireTests
        // already do against `_memory`, a same-shape unconditional field.

        [Test]
        public void Wire_Ambiguous_IsCompleteNoOpEvenWithAValidExistingChild()
        {
            var existingChild = _scope.CreateGameObject("TsInstance");
            existingChild.transform.SetParent(_root.transform, false);
            existingChild.AddComponent<StateManager>();

            var module = ModuleWith(typeof(StateManager), ambiguous: true);
            module.Wire();

            Assert.AreEqual(1, _root.transform.childCount, "Ambiguous must leave the existing child untouched.");
            Assert.IsNotNull(_root.transform.Find("TsInstance").GetComponent<StateManager>(), "Ambiguous must leave the existing component untouched.");
        }

        [Test]
        public void Wire_NoDetectedType_RemovesExistingChildAndClearsField()
        {
            var existingChild = _scope.CreateGameObject("TsInstance");
            existingChild.transform.SetParent(_root.transform, false);
            existingChild.AddComponent<StateManager>();

            var module = ModuleWith(null, ambiguous: false);
            module.Wire();

            Assert.IsNull(_root.transform.Find("TsInstance"));
            Assert.IsNull(InstanceFieldValue());
        }

        [Test]
        public void Wire_DetectedTypeSetNoExistingChild_CreatesChildWithComponent()
        {
            var module = ModuleWith(typeof(StateManager), ambiguous: false);

            module.Wire();

            var child = _root.transform.Find("TsInstance");
            Assert.IsNotNull(child);
            Assert.IsNotNull(child.GetComponent<StateManager>());
        }

        [Test]
        public void Wire_ExistingChildWithProgramAssetPresent_SecondWireIsIdempotent()
        {
            var module = ModuleWith(typeof(StateManager), ambiguous: false);
            module.Wire();
            var firstComponent = _root.transform.Find("TsInstance").GetComponent<StateManager>();

            module.Wire();

            var child = _root.transform.Find("TsInstance");
            Assert.AreEqual(1, _root.transform.childCount);
            Assert.AreEqual(firstComponent, child.GetComponent<StateManager>(), "Re-wiring an already-correct child must not recreate it.");
        }

        [Test]
        public void Wire_ProgramAssetDeletedWhileComponentPresent_StillRecreates()
        {
            var module = ModuleWith(typeof(StateManager), ambiguous: false);
            module.Wire();

            AssetDatabase.DeleteAsset(StateManagerAssetPath);

            module.Wire();

            var child = _root.transform.Find("TsInstance");
            Assert.IsNotNull(child);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(StateManagerAssetPath), "Program asset must be recreated.");
            Assert.IsNotNull(child.GetComponent<StateManager>());
        }
    }
}
