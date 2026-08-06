using NUnit.Framework;
using System;
using Tsvrc.Editor;
using Tsvrc.Testing.Framework;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // Tests InstanceModule.Wire() against a real compiled root. Creating a real child and
    // component (CreateComponent) needs `_detectedType` to be a genuine, already-compiled
    // UdonSharpBehaviour with a real script asset. Using an actual production
    // Instance subclass would work, but this project intentionally has none yet, and
    // adding one as a permanent test double would change TsGenerator.HasBootstrapSignal()
    // for the whole project, not just tests (InstanceModule doesn't care whether the
    // "detected" type is actually an Instance subclass for the plumbing exercised here,
    // only DetectInstanceType() does, and that isn't under test in this file). So
    // InstanceModuleWireTestDouble stands in as a real, already-compiled UdonSharpBehaviour:
    // CreateComponent()'s mechanics (script lookup, program asset creation, component add)
    // don't care that it isn't an Instance.
    //
    // The program asset CreateComponent() creates lives under TsPaths.GeneratedFolder,
    // redirected to a scratch folder here (same seam every other Wire()-level test uses) rather
    // than the real Assets/TsGenerated, so there is no manual byte backup/restore of a real
    // project file, and no crash-corruption risk if a test run is interrupted mid-test.
    public class InstanceModuleWireTests
    {
        private static string DetectedTypeAssetPath => $"{TsPaths.GeneratedFolder}/{nameof(InstanceModuleWireTestDouble)}.asset";

        private TempSceneScope _scope;
        private Component _root;

        [SetUp]
        public void SetUp()
        {
            _scope = new TempSceneScope();
            ScratchAssets.EnsureFolder();
            // AssetDatabase.CreateAsset (used by EnsureUdonSharpProgramAsset below, via
            // InstanceModule.CreateComponent) requires its target folder to already be a
            // recognized Unity asset folder, unlike TsGenerator's own file writes, which go
            // through a plain File.WriteAllText + Refresh that can discover a brand-new
            // subfolder in one pass. Pointing straight at ScratchAssets.Folder (already ensured
            // above) avoids introducing an unrecognized nested subfolder.
            TsPaths.GeneratedFolder = ScratchAssets.Folder;
            _root = CompiledRootFixture.AddTo(_scope);
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose(); // also resets TsPaths.GeneratedFolder
            ScratchAssets.DeleteAll();
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

        // Note: `_instance` is declared as `Instance`, and `InstanceModuleWireTestDouble` (the
        // real, already-compiled stand-in type used throughout this file, see the class
        // comment) does not extend Instance. Unity's SerializedProperty.objectReferenceValue
        // setter silently clamps an incompatible-type assignment to null rather than
        // throwing, so the field value can't be faithfully asserted against an
        // InstanceModuleWireTestDouble instance here. Only the scene-structure half
        // (child/component creation) can be. Confirming the field-assignment *mechanism* itself (a
        // compatible-type value really does get assigned) is exactly what
        // MemoryModuleWireTests/GlobalModuleWireTests already do against `_memory`, a
        // same-shape unconditional field.

        [Test]
        public void Wire_Ambiguous_IsCompleteNoOpEvenWithAValidExistingChild()
        {
            var existingChild = _scope.CreateGameObject("Instance");
            existingChild.transform.SetParent(_root.transform, false);
            existingChild.AddComponent<InstanceModuleWireTestDouble>();

            var module = ModuleWith(typeof(InstanceModuleWireTestDouble), ambiguous: true);
            module.Wire();

            Assert.AreEqual(1, _root.transform.childCount, "Ambiguous must leave the existing child untouched.");
            Assert.IsNotNull(_root.transform.Find("Instance").GetComponent<InstanceModuleWireTestDouble>(), "Ambiguous must leave the existing component untouched.");
        }

        [Test]
        public void Wire_NoDetectedType_RemovesExistingChildAndClearsField()
        {
            var existingChild = _scope.CreateGameObject("Instance");
            existingChild.transform.SetParent(_root.transform, false);
            existingChild.AddComponent<InstanceModuleWireTestDouble>();

            var module = ModuleWith(null, ambiguous: false);
            module.Wire();

            Assert.IsNull(_root.transform.Find("Instance"));
            Assert.IsNull(InstanceFieldValue());
        }

        [Test]
        public void Wire_DetectedTypeSetNoExistingChild_CreatesChildWithComponent()
        {
            var module = ModuleWith(typeof(InstanceModuleWireTestDouble), ambiguous: false);

            module.Wire();

            var child = _root.transform.Find("Instance");
            Assert.IsNotNull(child);
            Assert.IsNotNull(child.GetComponent<InstanceModuleWireTestDouble>());
        }

        [Test]
        public void Wire_ExistingChildWithProgramAssetPresent_SecondWireIsIdempotent()
        {
            var module = ModuleWith(typeof(InstanceModuleWireTestDouble), ambiguous: false);
            module.Wire();
            var firstComponent = _root.transform.Find("Instance").GetComponent<InstanceModuleWireTestDouble>();

            module.Wire();

            var child = _root.transform.Find("Instance");
            Assert.AreEqual(1, _root.transform.childCount);
            Assert.AreEqual(firstComponent, child.GetComponent<InstanceModuleWireTestDouble>(), "Re-wiring an already-correct child must not recreate it.");
        }

        // When the detected type changes (a retype/rename), only a stale Instance subclass is
        // destroyed. A genuinely unrelated, hand-attached UdonSharpBehaviour sharing the same
        // child GameObject must survive untouched.
        [Test]
        public void Wire_ForeignNonInstanceComponentOnChild_SurvivesADetectedTypeChange()
        {
            var existingChild = _scope.CreateGameObject("Instance");
            existingChild.transform.SetParent(_root.transform, false);
            var foreign = existingChild.AddComponent<InstanceModuleWireTestDouble>();

            // _detectedType is InstanceModuleWireStaleTestDouble, a real Instance subclass -
            // distinct from the foreign component's own type, so component == null (no matching
            // component yet) and Wire() enters its recreate branch.
            var module = ModuleWith(typeof(InstanceModuleWireStaleTestDouble), ambiguous: false);
            module.Wire();

            Assert.AreSame(foreign, existingChild.GetComponent<InstanceModuleWireTestDouble>(),
                "The foreign, non-Instance component must survive exactly as it was - not destroyed, not replaced.");
            Assert.IsNotNull(existingChild.GetComponent<InstanceModuleWireStaleTestDouble>(),
                "The newly detected type must still be created alongside it.");
        }

        [Test]
        public void Wire_StaleInstanceSubclassOnChild_IsDestroyedOnDetectedTypeChange()
        {
            var firstPass = ModuleWith(typeof(InstanceModuleWireStaleTestDouble), ambiguous: false);
            firstPass.Wire();
            var staleChild = _root.transform.Find("Instance");
            var stale = staleChild.GetComponent<InstanceModuleWireStaleTestDouble>();
            Assert.IsNotNull(stale, "Precondition: the old Instance subclass must exist before the retype.");

            // Simulates a rename/retype: the detected type is now InstanceModuleWireTestDouble
            // instead - stale, an Instance subclass, must be destroyed, unlike the foreign case above.
            var retyped = ModuleWith(typeof(InstanceModuleWireTestDouble), ambiguous: false);
            retyped.Wire();

            var child = _root.transform.Find("Instance");
            Assert.IsNull(child.GetComponent<InstanceModuleWireStaleTestDouble>(), "The stale Instance subclass must be destroyed.");
            Assert.IsNotNull(child.GetComponent<InstanceModuleWireTestDouble>(), "The newly detected type must be created.");
        }

        [Test]
        public void Wire_ProgramAssetDeletedWhileComponentPresent_StillRecreates()
        {
            var module = ModuleWith(typeof(InstanceModuleWireTestDouble), ambiguous: false);
            module.Wire();

            AssetDatabase.DeleteAsset(DetectedTypeAssetPath);

            module.Wire();

            var child = _root.transform.Find("Instance");
            Assert.IsNotNull(child);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<UdonSharp.UdonSharpProgramAsset>(DetectedTypeAssetPath), "Program asset must be recreated.");
            Assert.IsNotNull(child.GetComponent<InstanceModuleWireTestDouble>());
        }
    }
}
