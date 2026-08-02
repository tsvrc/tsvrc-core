using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;
using Tsvrc.StateMachine;
using Tsvrc.Testing.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.EditMode
{
    // TsModule.AliasName and Deduplicate are protected static, so InternalsVisibleTo alone
    // doesn't reach them. This test-only subclass exposes them for direct testing.
    internal sealed class TsModuleTestHarness : TsModule
    {
        internal override void LoadConfig()
        {
        }

        internal static string CallAliasName(string goName) => AliasName(goName);

        internal static string CallDeduplicate(string baseName, HashSet<string> usedNames)
            => Deduplicate(baseName, usedNames);

        internal static bool CallIsTsvrcBehaviourType(string shortName, string ns)
            => IsTsvrcBehaviourType(shortName, ns);

        internal static bool CallTryResolveObjectType(UnityEngine.Object obj, out string typeName, out string ns)
            => TryResolveObjectType(obj, out typeName, out ns);

        internal static bool CallTryResolveViaScript(Component component, out string typeName, out string ns)
            => TryResolveViaScript(component, out typeName, out ns);

        // Uses a plain string as TEntry, since the fallback logic itself doesn't care about
        // entry shape, only about counts and the two conversion delegates, so a string keeps
        // these tests focused on ApplySnapshotFallback's own branching instead of any real
        // module's entry struct.
        internal static List<string> CallApplySnapshotFallback(string moduleKey, List<string> resolved, int treeShakingExclusions = 0)
            => ApplySnapshotFallback(moduleKey, resolved,
                s => new ModuleEntrySnapshot.Entry { Name = s, TypeName = s, Namespace = "" },
                e => e.Name,
                treeShakingExclusions);

        // Uses a plain string as TEntry here too, same reasoning as CallApplySnapshotFallback
        // above: ApplyTreeShaking's own branching (config null/off, force-include, referenced,
        // grace period) doesn't care about entry shape, only about the name each entry maps to.
        internal static List<string> CallApplyTreeShaking(TsConfig config, List<string> resolved,
            System.Func<string, bool> isReferenced, out int excludedCount, out List<string> excludedNames, out List<string> graceIncludedNames,
            bool countsForGracePeriod = true)
            => ApplyTreeShaking(config, "TestModule", resolved, s => s, isReferenced, out excludedCount, out excludedNames, out graceIncludedNames,
                countsForGracePeriod);

        internal static string CallBuildStub(IEnumerable<string> usings, params string[] emptyMethodSignatures)
            => BuildStub(usings, emptyMethodSignatures);

        internal static bool CallTryFindField(SerializedObject so, string fieldName, string moduleTag, out SerializedProperty prop)
            => TryFindField(so, fieldName, moduleTag, out prop);

        internal static bool CallTryAcceptEntry(UnityEngine.Object obj, string moduleTag, string configLabel, string entryNoun, HashSet<UnityEngine.Object> seen)
            => TryAcceptEntry(obj, moduleTag, configLabel, entryNoun, seen);
    }

    public class TsModuleHelpersTests
    {
        [Test]
        public void AliasName_WithWellFormedAlias_ExtractsInnerText()
        {
            Assert.AreEqual("Foo", TsModuleTestHarness.CallAliasName("__Foo__"));
        }

        [Test]
        public void AliasName_WithNull_ReturnsNull()
        {
            Assert.IsNull(TsModuleTestHarness.CallAliasName(null));
        }

        [Test]
        public void AliasName_NotStartingWithDoubleUnderscore_ReturnsNull()
        {
            Assert.IsNull(TsModuleTestHarness.CallAliasName("Foo__"));
        }

        [Test]
        public void AliasName_NotEndingWithDoubleUnderscore_ReturnsNull()
        {
            Assert.IsNull(TsModuleTestHarness.CallAliasName("__Foo"));
        }

        [Test]
        public void AliasName_ExactlyFourUnderscores_ReturnsNull()
        {
            // "____" starts and ends with "__" but Length == 4, failing the `> 4` guard.
            Assert.IsNull(TsModuleTestHarness.CallAliasName("____"));
        }

        [Test]
        public void AliasName_FiveUnderscores_ExtractsSingleCharacter()
        {
            // Boundary one past the length-4 rejection: "_____" (5 chars) yields "_".
            Assert.AreEqual("_", TsModuleTestHarness.CallAliasName("_____"));
        }

        [Test]
        public void AliasName_JustDoubleUnderscore_ReturnsNull()
        {
            // "__" trivially starts and ends with itself but is far too short (Length == 2).
            Assert.IsNull(TsModuleTestHarness.CallAliasName("__"));
        }

        [Test]
        public void Deduplicate_NameNotInUsedSet_ReturnsNameUnchanged()
        {
            var used = new HashSet<string>();

            Assert.AreEqual("Foo", TsModuleTestHarness.CallDeduplicate("Foo", used));
        }

        [Test]
        public void Deduplicate_SingleCollision_AppendsSuffixStartingAtTwo()
        {
            var used = new HashSet<string> { "Foo" };

            Assert.AreEqual("Foo2", TsModuleTestHarness.CallDeduplicate("Foo", used));
        }

        [Test]
        public void Deduplicate_ChainedCollisions_IncrementsSuffixUntilFree()
        {
            var used = new HashSet<string> { "Foo", "Foo2", "Foo3" };

            Assert.AreEqual("Foo4", TsModuleTestHarness.CallDeduplicate("Foo", used));
        }

        [Test]
        public void IsTsvrcBehaviourType_RealTsvrcBehaviourSubclass_ReturnsTrue()
        {
            // Tsvrc.StateMachine.StateManager extends TsvrcBehaviour as a real production type,
            // not a test double, so this exercises the actual inheritance chain in this project.
            Assert.IsTrue(TsModuleTestHarness.CallIsTsvrcBehaviourType("StateManager", "Tsvrc.StateMachine"));
        }

        [Test]
        public void IsTsvrcBehaviourType_UdonSharpBehaviourNotExtendingTsvrcBehaviour_ReturnsFalse()
        {
            // TsRoot extends UdonSharpBehaviour directly, never TsvrcBehaviour.
            Assert.IsFalse(TsModuleTestHarness.CallIsTsvrcBehaviourType("TsRoot", "Tsvrc.Core.Generated"));
        }

        [Test]
        public void IsTsvrcBehaviourType_TypeNotFoundInAnyAssembly_ReturnsFalse()
        {
            Assert.IsFalse(TsModuleTestHarness.CallIsTsvrcBehaviourType("NoSuchTypeAnywhere_XyzZy", "No.Such.Namespace"));
        }

        [Test]
        public void IsTsvrcBehaviourType_NullOrEmptyNamespace_StillResolvesFullName()
        {
            // A null or empty ns both fall back to the bare short name in the full name lookup,
            // which correctly fails to resolve via reflection, since no type is literally named
            // "StateManager" with no namespace. Seeding ScriptIndex empty isolates this from its
            // real project fallback, which would otherwise, correctly, per its own tests, still
            // resolve the real, unambiguous StateManager to TsvrcBehaviour relationship by
            // source scan even with no namespace hint. That is a different, already covered
            // behavior, not what this test is about.
            SeedScriptIndex();

            Assert.IsTrue(TsModuleTestHarness.CallIsTsvrcBehaviourType("StateManager", "Tsvrc.StateMachine"));
            Assert.IsFalse(TsModuleTestHarness.CallIsTsvrcBehaviourType("StateManager", null));
            Assert.IsFalse(TsModuleTestHarness.CallIsTsvrcBehaviourType("StateManager", ""));
        }

        // The ScriptIndex fallback is exercised via a name guaranteed not to be a loaded type,
        // so the reflection fast path's assembly scan finds nothing and falls through, with
        // ScriptIndex's own state seeded directly rather than depending on real project source.
        // See ScriptIndexTests for ScriptIndex's own parsing and chain-walk coverage.
        [TearDown]
        public void TearDown() => PrivateFieldAccess.SetField(typeof(ScriptIndex), "_baseByClass", null);

        private static void SeedScriptIndex(params (string Name, string BaseSimpleName)[] links)
        {
            var map = new Dictionary<string, List<ScriptIndex.ClassInfo>>(System.StringComparer.Ordinal);
            foreach (var (name, baseName) in links)
                map[name] = new List<ScriptIndex.ClassInfo> { new ScriptIndex.ClassInfo("", baseName) };
            PrivateFieldAccess.SetField(typeof(ScriptIndex), "_baseByClass", map);
        }

        [Test]
        public void IsTsvrcBehaviourType_TypeNotLoaded_FallsBackToScriptIndex_ReturnsTrue()
        {
            SeedScriptIndex(("NotYetCompiledManager", "TsvrcBehaviour"));

            Assert.IsTrue(TsModuleTestHarness.CallIsTsvrcBehaviourType("NotYetCompiledManager", "Some.Namespace"));
        }

        [Test]
        public void IsTsvrcBehaviourType_TypeNotLoaded_FallsBackToScriptIndex_MultiHopChain_ReturnsTrue()
        {
            // Mirrors the chain InstanceManager to StateManager to TsvrcBehaviour, the exact
            // shape that broke in the field: a not yet compiled world script whose base is
            // itself another, in this scenario not yet compiled, framework class.
            SeedScriptIndex(("NotYetCompiledManager", "SomeMiddleClass"), ("SomeMiddleClass", "TsvrcBehaviour"));

            Assert.IsTrue(TsModuleTestHarness.CallIsTsvrcBehaviourType("NotYetCompiledManager", "Some.Namespace"));
        }

        [Test]
        public void IsTsvrcBehaviourType_TypeNotLoaded_ScriptIndexSaysUnrelated_ReturnsFalse()
        {
            SeedScriptIndex(("NotYetCompiledManager", "MonoBehaviour"));

            Assert.IsFalse(TsModuleTestHarness.CallIsTsvrcBehaviourType("NotYetCompiledManager", "Some.Namespace"));
        }

        [Test]
        public void IsTsvrcBehaviourType_TypeIsLoaded_ReflectionAnswerWinsEvenIfScriptIndexDisagrees()
        {
            // A real, loaded type, StateManager extending TsvrcBehaviour, must resolve via
            // reflection and never even consult ScriptIndex. This seeds it with a deliberately
            // contradictory answer to prove the fast path short-circuits first.
            SeedScriptIndex(("StateManager", "MonoBehaviour"));

            Assert.IsTrue(TsModuleTestHarness.CallIsTsvrcBehaviourType("StateManager", "Tsvrc.StateMachine"));
        }

        // ApplySnapshotFallback uses a fresh TempSceneScope purely for its reset-on-dispose
        // behavior on TsPaths, redirecting GeneratedFolder so ModuleEntrySnapshot's real file
        // I/O never touches a consuming project's actual Assets/TsGenerated.
        private const string FallbackKey = "TsModuleHelpersTestsFallback";
        private TempSceneScope _fallbackScope;

        [SetUp]
        public void SetUpFallbackScope()
        {
            _fallbackScope = new TempSceneScope();
            ScratchAssets.EnsureFolder();
            TsPaths.GeneratedFolder = ScratchAssets.Folder + "/ApplySnapshotFallbackScratch";
        }

        [TearDown]
        public void TearDownFallbackScope()
        {
            ModuleEntrySnapshot.Clear(FallbackKey);
            _fallbackScope.Dispose();
            ScratchAssets.DeleteAll();
        }

        [Test]
        public void ApplySnapshotFallback_CompilesClean_ReturnsLiveResultAndSavesIt()
        {
            TsPaths.ScriptCompilationFailedOverride = false;

            var result = TsModuleTestHarness.CallApplySnapshotFallback(FallbackKey, new List<string> { "A", "B" });

            CollectionAssert.AreEqual(new[] { "A", "B" }, result);
            var saved = ModuleEntrySnapshot.Load(FallbackKey);
            Assert.AreEqual(2, saved.Count);
        }

        [Test]
        public void ApplySnapshotFallback_CompileErrorsAndLiveResultSmallerThanSnapshot_ReturnsSnapshot()
        {
            TsPaths.ScriptCompilationFailedOverride = false;
            TsModuleTestHarness.CallApplySnapshotFallback(FallbackKey, new List<string> { "A", "B", "C" });

            TsPaths.ScriptCompilationFailedOverride = true;
            var result = TsModuleTestHarness.CallApplySnapshotFallback(FallbackKey, new List<string>());

            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, result);
        }

        [Test]
        public void ApplySnapshotFallback_CompileErrorsAndLiveResultEqualOrLarger_ReturnsLiveResult()
        {
            TsPaths.ScriptCompilationFailedOverride = false;
            TsModuleTestHarness.CallApplySnapshotFallback(FallbackKey, new List<string> { "A" });

            TsPaths.ScriptCompilationFailedOverride = true;
            var result = TsModuleTestHarness.CallApplySnapshotFallback(FallbackKey, new List<string> { "A", "B" });

            CollectionAssert.AreEqual(new[] { "A", "B" }, result);
        }

        [Test]
        public void ApplySnapshotFallback_CompileErrorsAndLiveResultExactlyEqualsCachedCount_ReturnsLiveResult()
        {
            // Pins the exact tie boundary. The comparison is cached.Count > resolved.Count,
            // strictly greater, not greater than or equal, so an equal count trusts the live
            // result even though its actual entries differ from the cached ones. A same-size
            // legitimate swap, one singleton replaced by another, must not be masked by the
            // stale cache.
            TsPaths.ScriptCompilationFailedOverride = false;
            TsModuleTestHarness.CallApplySnapshotFallback(FallbackKey, new List<string> { "A", "B" });

            TsPaths.ScriptCompilationFailedOverride = true;
            var result = TsModuleTestHarness.CallApplySnapshotFallback(FallbackKey, new List<string> { "C", "D" });

            CollectionAssert.AreEqual(new[] { "C", "D" }, result);
        }

        [Test]
        public void ApplySnapshotFallback_CompileErrorsAndNoSnapshotSaved_ReturnsLiveResultUnchanged()
        {
            TsPaths.ScriptCompilationFailedOverride = true;

            var result = TsModuleTestHarness.CallApplySnapshotFallback(FallbackKey, new List<string>());

            Assert.IsEmpty(result);
        }

        [Test]
        public void ApplySnapshotFallback_CompileErrorsDoNotOverwriteTheSnapshotFile()
        {
            TsPaths.ScriptCompilationFailedOverride = false;
            TsModuleTestHarness.CallApplySnapshotFallback(FallbackKey, new List<string> { "A", "B" });

            TsPaths.ScriptCompilationFailedOverride = true;
            TsModuleTestHarness.CallApplySnapshotFallback(FallbackKey, new List<string>());

            // Confirms the fallback path is read only with respect to the snapshot file itself.
            // A broken compile pass must never persist its own, untrustworthy, reduced result.
            Assert.AreEqual(2, ModuleEntrySnapshot.Load(FallbackKey).Count);
        }

        // A clean compile with fewer entries than last time is, by design (see the tests above),
        // still respected as the live, correct result. This group covers the loud warning
        // layered on top, for the dangerous case: an accidental out-of-band deletion (TsConfig
        // removed from the Hierarchy) that also compiles clean and looks like an ordinary
        // regenerate.
        [Test]
        public void ApplySnapshotFallback_CleanCompileReducedBelowLastKnownGood_WarnsButStillReturnsLiveResult()
        {
            TsPaths.ScriptCompilationFailedOverride = false;
            TsModuleTestHarness.CallApplySnapshotFallback(FallbackKey, new List<string> { "A", "B", "C" });

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                $@"\[{FallbackKey}\] This regenerate resolved fewer entries \(1\) than the last known-good count \(3\)"));
            var result = TsModuleTestHarness.CallApplySnapshotFallback(FallbackKey, new List<string> { "X" });

            CollectionAssert.AreEqual(new[] { "X" }, result,
                "The warning must not block the pass - a real, deliberate deletion via Configure's own " +
                "\"-\" button must stay fast, with no confirmation dialog blocking generation.");
        }

        [Test]
        public void ApplySnapshotFallback_CleanCompileCountStaysTheSameOrGrows_NoWarning()
        {
            TsPaths.ScriptCompilationFailedOverride = false;
            TsModuleTestHarness.CallApplySnapshotFallback(FallbackKey, new List<string> { "A", "B" });

            // LogAssert has nothing to Expect here - any unexpected warning would fail the test
            // on its own via Unity's default "unhandled log message" test failure behavior.
            var result = TsModuleTestHarness.CallApplySnapshotFallback(FallbackKey, new List<string> { "A", "B", "C" });

            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, result);
        }

        [Test]
        public void ApplySnapshotFallback_FirstEverCleanPass_NoWarningEvenIfEmpty()
        {
            TsPaths.ScriptCompilationFailedOverride = false;

            // No prior last-known-good on record: an empty first pass on a fresh project is the
            // ordinary, harmless default state, not a regression from anything.
            var result = TsModuleTestHarness.CallApplySnapshotFallback(FallbackKey, new List<string>());

            Assert.IsEmpty(result);
        }

        [Test]
        public void ApplySnapshotFallback_LastKnownGoodIsAHighWaterMark_DoesNotDropAfterARegression()
        {
            TsPaths.ScriptCompilationFailedOverride = false;
            TsModuleTestHarness.CallApplySnapshotFallback(FallbackKey, new List<string> { "A", "B", "C" }); // peak: 3

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*"));
            TsModuleTestHarness.CallApplySnapshotFallback(FallbackKey, new List<string> { "X" }); // regression to 1, warns once

            // A second, later pass at 2 entries must still warn relative to the original peak of
            // 3, not silently accept 1 as the new baseline just because the last pass reported it.
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                $@"\[{FallbackKey}\] This regenerate resolved fewer entries \(2\) than the last known-good count \(3\)"));
            TsModuleTestHarness.CallApplySnapshotFallback(FallbackKey, new List<string> { "Y", "Z" });
        }

        // TryResolveObjectType/TryResolveViaScript: resolving a live Object's type name +
        // namespace, falling back to ScriptIndex (via the component's own MonoScript) when
        // reflection can't produce a real answer. See ScriptIndexTests for TryResolveDeclaredType's
        // own ambiguous-name and fallback-parsing coverage.
        //
        // Deliberately reuses _fallbackScope (already created per-test by SetUpFallbackScope)
        // instead of its own TempSceneScope: a second scope's Dispose() would call
        // TsPaths.ResetToDefaults() unconditionally too, and depending on [TearDown] ordering
        // that can fire before TearDownFallbackScope's own ModuleEntrySnapshot.Clear(), making it
        // clear the wrong (already-reset-to-default) folder and leak snapshot state into a later
        // ApplySnapshotFallback test - exactly the kind of cross-test leak these scopes exist to
        // prevent.
        [Test]
        public void TryResolveObjectType_Null_ReturnsFalse()
        {
            Assert.IsFalse(TsModuleTestHarness.CallTryResolveObjectType(null, out _, out _));
        }

        [Test]
        public void TryResolveObjectType_GameObject_ResolvesAsGameObjectViaFastPath()
        {
            var go = _fallbackScope.CreateGameObject("Anything");

            bool result = TsModuleTestHarness.CallTryResolveObjectType(go, out string typeName, out string ns);

            Assert.IsTrue(result);
            Assert.AreEqual("GameObject", typeName);
            Assert.AreEqual("UnityEngine", ns);
        }

        [Test]
        public void TryResolveObjectType_LiveCompiledComponent_ResolvesViaReflectionFastPath()
        {
            // StateManager is a real, always-compiled library type, so this exercises the fast
            // path (obj.GetType() already gives a real concrete type) without needing the
            // ScriptIndex fallback at all.
            var go = _fallbackScope.CreateGameObject("Anything");
            var behaviour = go.AddComponent<StateManager>();

            bool result = TsModuleTestHarness.CallTryResolveObjectType(behaviour, out string typeName, out string ns);

            Assert.IsTrue(result);
            Assert.AreEqual("StateManager", typeName);
            Assert.AreEqual("Tsvrc.StateMachine", ns);
        }

        [Test]
        public void TryResolveViaScript_NullComponent_ReturnsFalse()
        {
            Assert.IsFalse(TsModuleTestHarness.CallTryResolveViaScript(null, out _, out _));
        }

        [Test]
        public void TryResolveViaScript_RealComponent_ResolvesItsOwnMonoScriptViaScriptIndex()
        {
            // Exercises the m_Script -> MonoScript -> ScriptIndex coupling directly against a
            // real component's real backing script, independent of whether GetType() itself
            // would have needed the fallback: Unity provides no supported way to construct an
            // actual "Missing (Mono Script)" component from editor script to drive this
            // end-to-end (AddComponent<MonoBehaviour> is rejected as abstract-for-attachment).
            var go = _fallbackScope.CreateGameObject("Anything");
            var behaviour = go.AddComponent<StateManager>();

            bool result = TsModuleTestHarness.CallTryResolveViaScript(behaviour, out string typeName, out string ns);

            Assert.IsTrue(result);
            Assert.AreEqual("StateManager", typeName);
            Assert.AreEqual("Tsvrc.StateMachine", ns);
        }

        [Test]
        public void BuildStub_NoUsingsNoMethods_OmitsUsingBlockAndWritesEmptyClass()
        {
            string code = TsModuleTestHarness.CallBuildStub(null);

            StringAssert.DoesNotContain("using ", code);
            StringAssert.Contains($"public partial class {ScaffoldModule.CompiledClassName}", code);
        }

        [Test]
        public void BuildStub_WithUsingsAndOneMethod_WritesUsingsAndEmptyMethodBody()
        {
            string code = TsModuleTestHarness.CallBuildStub(new[] { "UdonSharp", "UnityEngine" }, "public void _TsPoolStart()");

            StringAssert.Contains("using UdonSharp;", code);
            StringAssert.Contains("using UnityEngine;", code);
            StringAssert.Contains("public void _TsPoolStart()", code);
        }

        [Test]
        public void BuildStub_MultipleMethodSignatures_WritesEachOne()
        {
            string code = TsModuleTestHarness.CallBuildStub(null, "public void A()", "public void B()");

            StringAssert.Contains("public void A()", code);
            StringAssert.Contains("public void B()", code);
        }

        [Test]
        public void TryFindField_FieldExists_ReturnsTrueWithProperty()
        {
            var behaviour = _fallbackScope.CreateGameObject("Anything").AddComponent<StateManager>();
            var so = new SerializedObject(behaviour);

            bool result = TsModuleTestHarness.CallTryFindField(so, "m_Enabled", "SomeModule", out var prop);

            Assert.IsTrue(result);
            Assert.IsNotNull(prop);
        }

        [Test]
        public void TryFindField_FieldMissing_LogsForceCompileWarningAndReturnsFalse()
        {
            var behaviour = _fallbackScope.CreateGameObject("Anything").AddComponent<StateManager>();
            var so = new SerializedObject(behaviour);

            LogAssert.Expect(LogType.Warning, "[SomeModule] Field 'NoSuchField' not found on TsGenerated. Force compile to regenerate.");
            bool result = TsModuleTestHarness.CallTryFindField(so, "NoSuchField", "SomeModule", out var prop);

            Assert.IsFalse(result);
            Assert.IsNull(prop);
        }

        [Test]
        public void TryAcceptEntry_NullObject_LogsNullEntryWarningWithGivenConfigLabelAndReturnsFalse()
        {
            var seen = new HashSet<UnityEngine.Object>();

            LogAssert.Expect(LogType.Warning, "[SomeModule] Null entry in Constructs config, remove the missing-script slot.");
            bool result = TsModuleTestHarness.CallTryAcceptEntry(null, "SomeModule", "Constructs config", "construct", seen);

            Assert.IsFalse(result);
        }

        [Test]
        public void TryAcceptEntry_DuplicateObject_LogsDuplicateWarningWithGivenEntryNounAndReturnsFalse()
        {
            var config = _fallbackScope.CreateGameObject("Dup").AddComponent<TsConfig>();
            var seen = new HashSet<UnityEngine.Object> { config };

            LogAssert.Expect(LogType.Warning, "[SomeModule] Duplicate construct 'Dup' in config, remove the duplicate.");
            bool result = TsModuleTestHarness.CallTryAcceptEntry(config, "SomeModule", "Constructs config", "construct", seen);

            Assert.IsFalse(result);
        }

        [Test]
        public void TryAcceptEntry_NewValidObject_ReturnsTrueAndAddsItToSeen()
        {
            var config = _fallbackScope.CreateGameObject("Fresh").AddComponent<TsConfig>();
            var seen = new HashSet<UnityEngine.Object>();

            bool result = TsModuleTestHarness.CallTryAcceptEntry(config, "SomeModule", "config", "entry", seen);

            Assert.IsTrue(result);
            Assert.IsTrue(seen.Contains(config));
        }

        // ApplyTreeShaking: the shared filter Singleton and Factory both route through instead of
        // hand-rolling their own, the same reasoning TryAcceptEntry already established for
        // null/duplicate handling above. Every "unreferenced" case below goes through
        // ConsumeGracePeriod's one-pass grace: an entry is only excluded once it's been observed
        // unreferenced on two separate calls, never the first.
        [Test]
        public void ApplyTreeShaking_ConfigNull_ReturnsResolvedUnchangedWithNoExclusions()
        {
            var result = TsModuleTestHarness.CallApplyTreeShaking(null, new List<string> { "A", "B" },
                _ => false, out int excluded, out var names, out var grace);

            CollectionAssert.AreEqual(new[] { "A", "B" }, result);
            Assert.AreEqual(0, excluded);
            Assert.IsEmpty(names);
            Assert.IsEmpty(grace);
        }

        [Test]
        public void ApplyTreeShaking_TreeShakeUnusedOff_ReturnsResolvedUnchangedEvenIfNothingIsReferenced()
        {
            var config = _fallbackScope.CreateGameObject("Cfg").AddComponent<TsConfig>();
            config.TreeShakeUnused = false;

            var result = TsModuleTestHarness.CallApplyTreeShaking(config, new List<string> { "A", "B" },
                _ => false, out int excluded, out var names, out var grace);

            CollectionAssert.AreEqual(new[] { "A", "B" }, result);
            Assert.AreEqual(0, excluded);
            Assert.IsEmpty(grace);
        }

        [Test]
        public void ApplyTreeShaking_OnAndReferenced_KeepsEntry()
        {
            var config = _fallbackScope.CreateGameObject("Cfg").AddComponent<TsConfig>();
            config.TreeShakeUnused = true;

            var result = TsModuleTestHarness.CallApplyTreeShaking(config, new List<string> { "A" },
                name => name == "A", out int excluded, out var names, out var grace);

            CollectionAssert.AreEqual(new[] { "A" }, result);
            Assert.AreEqual(0, excluded);
            Assert.IsEmpty(names);
            Assert.IsEmpty(grace);
        }

        // The core fix for the chicken-and-egg problem: a name resolved as unreferenced for the
        // very first time is kept (via the grace period), never excluded outright - otherwise a
        // just-registered entry could never be written against in code, since it would vanish
        // before a developer ever got the chance to reference it.
        [Test]
        public void ApplyTreeShaking_OnAndNotReferencedForTheFirstTime_KeepsEntryViaGracePeriodInsteadOfExcluding()
        {
            var config = _fallbackScope.CreateGameObject("Cfg").AddComponent<TsConfig>();
            config.TreeShakeUnused = true;

            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex(@"\[TestModule\] 1 entry isn't referenced"));
            var result = TsModuleTestHarness.CallApplyTreeShaking(config, new List<string> { "A" },
                _ => false, out int excluded, out var names, out var grace);

            CollectionAssert.AreEqual(new[] { "A" }, result, "First unreferenced observation must be kept, not excluded.");
            Assert.AreEqual(0, excluded);
            Assert.IsEmpty(names);
            CollectionAssert.AreEqual(new[] { "A" }, grace);
        }

        [Test]
        public void ApplyTreeShaking_OnAndNotReferencedForASecondConsecutivePass_ExcludesEntryAndLogsIt()
        {
            var config = _fallbackScope.CreateGameObject("Cfg").AddComponent<TsConfig>();
            config.TreeShakeUnused = true;
            // First pass consumes the grace period.
            TsModuleTestHarness.CallApplyTreeShaking(config, new List<string> { "A" }, _ => false, out _, out _, out _);

            LogAssert.Expect(LogType.Log, "[TestModule] Excluded 'A' - not referenced anywhere in the project (checked " +
                "across two regenerates) and not force-included. Reference it from a TsvrcBehaviour, or add it to " +
                "Force Include Names in Configure, to keep generating it.");
            var result = TsModuleTestHarness.CallApplyTreeShaking(config, new List<string> { "A" },
                _ => false, out int excluded, out var names, out var grace);

            Assert.IsEmpty(result);
            Assert.AreEqual(1, excluded);
            CollectionAssert.AreEqual(new[] { "A" }, names);
            Assert.IsEmpty(grace);
        }

        [Test]
        public void ApplyTreeShaking_ReferencedAfterAMissedPass_ResetsTheGracePeriodInsteadOfExcludingLater()
        {
            var config = _fallbackScope.CreateGameObject("Cfg").AddComponent<TsConfig>();
            config.TreeShakeUnused = true;
            // First pass: unreferenced, consumes grace.
            TsModuleTestHarness.CallApplyTreeShaking(config, new List<string> { "A" }, _ => false, out _, out _, out _);
            // Second pass: now referenced - must clear the miss-streak entirely, not just survive this one pass.
            TsModuleTestHarness.CallApplyTreeShaking(config, new List<string> { "A" }, name => name == "A", out _, out _, out _);

            // Third pass: unreferenced again - if the miss-streak weren't reset by the referenced
            // pass in between, this would exclude immediately; it must instead restart from zero.
            var result = TsModuleTestHarness.CallApplyTreeShaking(config, new List<string> { "A" },
                _ => false, out int excluded, out var names, out var grace);

            CollectionAssert.AreEqual(new[] { "A" }, result);
            Assert.AreEqual(0, excluded);
            CollectionAssert.AreEqual(new[] { "A" }, grace);
        }

        [Test]
        public void ApplyTreeShaking_OnAndForceIncluded_KeepsEntryEvenWhenNotReferenced()
        {
            var config = _fallbackScope.CreateGameObject("Cfg").AddComponent<TsConfig>();
            config.TreeShakeUnused = true;
            config.ForceIncludeNames = new[] { "A" };

            var result = TsModuleTestHarness.CallApplyTreeShaking(config, new List<string> { "A" },
                _ => false, out int excluded, out var names, out var grace);

            CollectionAssert.AreEqual(new[] { "A" }, result);
            Assert.AreEqual(0, excluded);
            Assert.IsEmpty(grace, "Force-included entries never even enter the grace-period bookkeeping.");
        }

        [Test]
        public void ApplyTreeShaking_MixOfReferencedGraceAndForceIncluded_KeepsAllThreeOnFirstPass()
        {
            var config = _fallbackScope.CreateGameObject("Cfg").AddComponent<TsConfig>();
            config.TreeShakeUnused = true;
            config.ForceIncludeNames = new[] { "Pinned" };

            var result = TsModuleTestHarness.CallApplyTreeShaking(config,
                new List<string> { "Referenced", "NewlyUnreferenced", "Pinned" },
                name => name == "Referenced", out int excluded, out var names, out var grace);

            CollectionAssert.AreEquivalent(new[] { "Referenced", "NewlyUnreferenced", "Pinned" }, result);
            Assert.AreEqual(0, excluded);
            Assert.IsEmpty(names);
            CollectionAssert.AreEqual(new[] { "NewlyUnreferenced" }, grace);
        }

        [Test]
        public void ApplyTreeShaking_MixOfReferencedAndNotOnASecondPass_ExcludesOnlyTheStillUnreferencedOne()
        {
            var config = _fallbackScope.CreateGameObject("Cfg").AddComponent<TsConfig>();
            config.TreeShakeUnused = true;
            config.ForceIncludeNames = new[] { "Pinned" };
            // First pass consumes "Dead"'s grace period.
            TsModuleTestHarness.CallApplyTreeShaking(config,
                new List<string> { "Referenced", "Dead", "Pinned" }, name => name == "Referenced", out _, out _, out _);

            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex(@"\[TestModule\] Excluded 'Dead'"));
            var result = TsModuleTestHarness.CallApplyTreeShaking(config,
                new List<string> { "Referenced", "Dead", "Pinned" },
                name => name == "Referenced", out int excluded, out var names, out var grace);

            CollectionAssert.AreEquivalent(new[] { "Referenced", "Pinned" }, result);
            Assert.AreEqual(1, excluded);
            CollectionAssert.AreEqual(new[] { "Dead" }, names);
            Assert.IsEmpty(grace);
        }

        // A pass caused only by a reactive trigger unrelated to a real recompile
        // (countsForGracePeriod: false) must still show an accurate, current result, but must
        // never consume or reset any entry's grace-period miss-streak, since the developer had no
        // real opportunity in that pass to reference the entry.
        [Test]
        public void ApplyTreeShaking_CountsForGracePeriodFalse_KeepsUnreferencedEntryEvenOnWhatWouldOtherwiseBeTheSecondMiss()
        {
            var config = _fallbackScope.CreateGameObject("Cfg").AddComponent<TsConfig>();
            config.TreeShakeUnused = true;
            // A real pass consumes the first miss.
            TsModuleTestHarness.CallApplyTreeShaking(config, new List<string> { "A" }, _ => false, out _, out _, out _, countsForGracePeriod: true);

            // A non-counting pass immediately after: would normally be the excluding second miss,
            // but must not exclude, since it doesn't count.
            var result = TsModuleTestHarness.CallApplyTreeShaking(config, new List<string> { "A" },
                _ => false, out int excluded, out var names, out var grace, countsForGracePeriod: false);

            CollectionAssert.AreEqual(new[] { "A" }, result, "A non-counting pass must never exclude an entry.");
            Assert.AreEqual(0, excluded);
            Assert.IsEmpty(names);
            CollectionAssert.AreEqual(new[] { "A" }, grace, "Still surfaced as grace-included, so nothing looks silently gone.");
        }

        [Test]
        public void ApplyTreeShaking_CountsForGracePeriodFalse_LeavesPersistedMissStreakUntouchedForTheNextRealPass()
        {
            var config = _fallbackScope.CreateGameObject("Cfg").AddComponent<TsConfig>();
            config.TreeShakeUnused = true;
            // First real pass consumes the first miss.
            TsModuleTestHarness.CallApplyTreeShaking(config, new List<string> { "A" }, _ => false, out _, out _, out _, countsForGracePeriod: true);
            // A non-counting pass in between must not reset or otherwise perturb that miss.
            TsModuleTestHarness.CallApplyTreeShaking(config, new List<string> { "A" }, _ => false, out _, out _, out _, countsForGracePeriod: false);

            // The next real pass must see this as the genuine second consecutive real miss and
            // exclude - proving the non-counting pass in between neither consumed nor reset state.
            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex(@"\[TestModule\] Excluded 'A'"));
            var result = TsModuleTestHarness.CallApplyTreeShaking(config, new List<string> { "A" },
                _ => false, out int excluded, out var names, out var grace, countsForGracePeriod: true);

            Assert.IsEmpty(result);
            Assert.AreEqual(1, excluded);
            CollectionAssert.AreEqual(new[] { "A" }, names);
        }

        // A broken compile anywhere in the project - not necessarily Tsvrc's own generated files -
        // means live resolution can't be trusted, mirroring ApplySnapshotFallback's own gate, even
        // though countsForGracePeriod itself is left at its default true.
        [Test]
        public void ApplyTreeShaking_ScriptCompilationFailed_DoesNotConsumeOrExcludeGraceEvenOnASecondPass()
        {
            var config = _fallbackScope.CreateGameObject("Cfg").AddComponent<TsConfig>();
            config.TreeShakeUnused = true;
            TsPaths.ScriptCompilationFailedOverride = false;
            // First pass, compile clean: consumes the first miss.
            TsModuleTestHarness.CallApplyTreeShaking(config, new List<string> { "A" }, _ => false, out _, out _, out _);

            TsPaths.ScriptCompilationFailedOverride = true;
            var result = TsModuleTestHarness.CallApplyTreeShaking(config, new List<string> { "A" },
                _ => false, out int excluded, out var names, out var grace);

            CollectionAssert.AreEqual(new[] { "A" }, result, "Must never exclude while the compile is broken.");
            Assert.AreEqual(0, excluded);
            Assert.IsEmpty(names);
            CollectionAssert.AreEqual(new[] { "A" }, grace);

            // Once clean again, the pre-break miss must still be exactly one, not reset and not
            // double-counted by the broken-compile pass in between.
            TsPaths.ScriptCompilationFailedOverride = false;
            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex(@"\[TestModule\] Excluded 'A'"));
            var finalResult = TsModuleTestHarness.CallApplyTreeShaking(config, new List<string> { "A" },
                _ => false, out int finalExcluded, out var finalNames, out _);

            Assert.IsEmpty(finalResult);
            Assert.AreEqual(1, finalExcluded);
            CollectionAssert.AreEqual(new[] { "A" }, finalNames);
        }

        // The attributed-drop reconciliation between ApplyTreeShaking's exclusions and
        // ApplySnapshotFallback's last-known-good warning: a drop fully explained by this pass's
        // own tree-shaking must not repeat the "accidental deletion" warning, but any unattributed
        // remainder still must.
        [Test]
        public void ApplySnapshotFallback_DropFullyExplainedByTreeShakingExclusions_NoWarning()
        {
            TsPaths.ScriptCompilationFailedOverride = false;
            TsModuleTestHarness.CallApplySnapshotFallback(FallbackKey, new List<string> { "A", "B", "C" });

            // Drop from 3 to 1 (two fewer), fully attributed to two tree-shaking exclusions this
            // pass - no LogAssert.Expect here, so any unexpected warning fails the test on its own.
            var result = TsModuleTestHarness.CallApplySnapshotFallback(FallbackKey, new List<string> { "X" }, treeShakingExclusions: 2);

            CollectionAssert.AreEqual(new[] { "X" }, result);
        }

        [Test]
        public void ApplySnapshotFallback_DropLargerThanTreeShakingExclusions_WarnsForTheUnattributedRemainder()
        {
            TsPaths.ScriptCompilationFailedOverride = false;
            TsModuleTestHarness.CallApplySnapshotFallback(FallbackKey, new List<string> { "A", "B", "C" });

            // Drop from 3 to 1 (two fewer), but only one is attributed to tree-shaking - the
            // remaining, unexplained one still needs the loud warning.
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                $@"\[{FallbackKey}\] This regenerate resolved fewer entries \(1\) than the last known-good count \(3\)"));
            TsModuleTestHarness.CallApplySnapshotFallback(FallbackKey, new List<string> { "X" }, treeShakingExclusions: 1);
        }
    }
}
