using System;
using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Editor;
using Tsvrc.Testing.Framework;

namespace Tsvrc.Tests.EditMode
{
    // Tests InstanceModule's ScriptIndex fallback. When the live AppDomain scan finds zero
    // Instance subclasses, typically because Assembly-CSharp is currently broken and the real
    // subclass, for example a world's MolInstance, failed to compile along with it, ScriptIndex
    // confirming a real subclass still exists in source must stop Wire() from destroying the
    // existing "Instance" child and nulling the scene field. There is no live Type to re-wire it
    // with either way, so the only thing this protects is not throwing away real, working state
    // on a transient failure. GenerateCode()'s output never depends on the detected type at all,
    // see InstanceModule._knownViaScriptIndexOnly's own doc comment, so there's nothing to test
    // there.
    public class InstanceModuleScriptIndexFallbackTests
    {
        [TearDown]
        public void TearDown() => PrivateFieldAccess.SetField(typeof(ScriptIndex), "_baseByClass", null);

        private static void SeedScriptIndex(params (string Name, string Namespace, string BaseSimpleName)[] classes)
        {
            var map = new Dictionary<string, List<ScriptIndex.ClassInfo>>(System.StringComparer.Ordinal);
            foreach (var (name, ns, baseName) in classes)
            {
                if (!map.TryGetValue(name, out var list))
                    map[name] = list = new List<ScriptIndex.ClassInfo>();
                list.Add(new ScriptIndex.ClassInfo(ns, baseName));
            }
            PrivateFieldAccess.SetField(typeof(ScriptIndex), "_baseByClass", map);
        }

        // Calls InstanceModule.ComputeKnownViaScriptIndexOnly directly with controlled
        // detectedType and ambiguous inputs rather than driving the real live AppDomain scan via
        // LoadConfig(). In a full solution run, Tsvrc plus a consuming project like MoL, a
        // real, un-ignored Instance subclass such as MolInstance may actually be loaded and
        // compiling fine, so "the live scan finds zero candidates" can't be reliably forced
        // from outside. This is the same reason IsTsvrcBehaviourType and ApplySnapshotFallback
        // are tested as directly callable static methods instead of only through LoadConfig().
        private static bool ComputeKnownViaScriptIndexOnly(Type detectedType, bool ambiguous)
            => InstanceModule.ComputeKnownViaScriptIndexOnly(detectedType, ambiguous);

        [Test]
        public void ComputeKnownViaScriptIndexOnly_NoLiveCandidateAndScriptIndexHasNone_ReturnsFalse()
        {
            SeedScriptIndex(); // empty, genuinely nothing anywhere

            Assert.IsFalse(ComputeKnownViaScriptIndexOnly(null, ambiguous: false));
        }

        [Test]
        public void ComputeKnownViaScriptIndexOnly_NoLiveCandidateButScriptIndexHasExactlyOne_ReturnsTrue()
        {
            SeedScriptIndex(("MolInstance", "MoL.Core", "Instance"));

            Assert.IsTrue(ComputeKnownViaScriptIndexOnly(null, ambiguous: false));
        }

        [Test]
        public void ComputeKnownViaScriptIndexOnly_NoLiveCandidateButScriptIndexHasTwo_DoesNotGuess_ReturnsFalse()
        {
            SeedScriptIndex(
                ("MolInstance", "MoL.Core", "Instance"),
                ("OtherInstance", "MoL.Other", "Instance"));

            Assert.IsFalse(ComputeKnownViaScriptIndexOnly(null, ambiguous: false));
        }

        [Test]
        public void ComputeKnownViaScriptIndexOnly_LiveCandidateDetected_NeverConsultsScriptIndex_ReturnsFalse()
        {
            // Even if ScriptIndex would say "yes" here, a real live detectedType means the
            // fallback path must not run at all. It exists only for the "zero live" case.
            SeedScriptIndex(("MolInstance", "MoL.Core", "Instance"));

            Assert.IsFalse(ComputeKnownViaScriptIndexOnly(typeof(object), ambiguous: false));
        }

        [Test]
        public void ComputeKnownViaScriptIndexOnly_LiveAmbiguous_NeverConsultsScriptIndex_ReturnsFalse()
        {
            SeedScriptIndex(("MolInstance", "MoL.Core", "Instance"));

            Assert.IsFalse(ComputeKnownViaScriptIndexOnly(null, ambiguous: true));
        }

        [Test]
        public void Wire_KnownViaScriptIndexOnly_DoesNotDestroyExistingInstanceChildOrNullField()
        {
            using var scope = new TempSceneScope();
            var root = CompiledRootFixture.AddTo(scope);
            var existingChild = scope.CreateGameObject("Instance");
            existingChild.transform.SetParent(root.transform, false);

            var module = new InstanceModule();
            PrivateFieldAccess.SetField(module, "_detectedType", null);
            PrivateFieldAccess.SetField(module, "_ambiguous", false);
            PrivateFieldAccess.SetField(module, "_knownViaScriptIndexOnly", true);

            module.Wire();

            Assert.IsNotNull(root.transform.Find("Instance"),
                "Wire() must not destroy the existing Instance child while a real subclass is only unreachable due to a (presumed) transient broken compile.");
        }

        [Test]
        public void Wire_GenuinelyNoCandidateAnywhere_StillDestroysExistingInstanceChildAsBefore()
        {
            using var scope = new TempSceneScope();
            var root = CompiledRootFixture.AddTo(scope);
            var existingChild = scope.CreateGameObject("Instance");
            existingChild.transform.SetParent(root.transform, false);

            var module = new InstanceModule();
            PrivateFieldAccess.SetField(module, "_detectedType", null);
            PrivateFieldAccess.SetField(module, "_ambiguous", false);
            PrivateFieldAccess.SetField(module, "_knownViaScriptIndexOnly", false);

            module.Wire();

            Assert.IsNull(root.transform.Find("Instance"),
                "Regression check: the pre-existing 'genuinely no Instance subclass' behavior must be unchanged.");
        }
    }
}
