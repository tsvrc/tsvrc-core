using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Editor;
using Tsvrc.Testing.Framework;

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

        // Uses a plain string as TEntry, since the fallback logic itself doesn't care about
        // entry shape, only about counts and the two conversion delegates, so a string keeps
        // these tests focused on ApplySnapshotFallback's own branching instead of any real
        // module's entry struct.
        internal static List<string> CallApplySnapshotFallback(string moduleKey, List<string> resolved)
            => ApplySnapshotFallback(moduleKey, resolved,
                s => new ModuleEntrySnapshot.Entry { Name = s, TypeName = s, Namespace = "" },
                e => e.Name);
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
    }
}
