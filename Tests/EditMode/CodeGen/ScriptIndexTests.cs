using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Editor;
using Tsvrc.Testing.Framework;

namespace Tsvrc.Tests.EditMode
{
    // ScriptIndex is a source-text based resolver answering whether class X derives from class
    // Y, used as TsModule.IsTsvrcBehaviourType's fallback when the type in question isn't
    // loaded, typically because Assembly-CSharp currently has a compile error elsewhere.
    public class ScriptIndexTests
    {
        [TearDown]
        public void TearDown()
        {
            // Never leave a test-injected map in place for a later test (or a later real
            // Rebuild() call within the same pass) to accidentally see.
            PrivateFieldAccess.SetField(typeof(ScriptIndex), "_baseByClass", null);
        }

        private static Dictionary<string, List<ScriptIndex.ClassInfo>> ParseOnly(string source)
        {
            var map = new Dictionary<string, List<ScriptIndex.ClassInfo>>(System.StringComparer.Ordinal);
            ScriptIndex.ParseInto(source, map);
            return map;
        }

        private static string BaseOf(Dictionary<string, List<ScriptIndex.ClassInfo>> map, string name, string ns = "")
        {
            var match = map[name].Find(c => c.Namespace == ns);
            return match.BaseSimpleName;
        }

        [Test]
        public void ParseInto_SimpleClassWithBase_RecordsSimpleBaseName()
        {
            var map = ParseOnly("namespace N { public class Foo : Bar { } }");

            Assert.AreEqual("Bar", BaseOf(map, "Foo", "N"));
        }

        [Test]
        public void ParseInto_NamespaceQualifiedBase_KeepsOnlyTheSimpleName()
        {
            var map = ParseOnly("public class Foo : Some.Deep.Namespace.Bar { }");

            Assert.AreEqual("Bar", BaseOf(map, "Foo"));
        }

        [Test]
        public void ParseInto_ClassWithNoBase_RecordsNull()
        {
            var map = ParseOnly("public class Foo { }");

            Assert.IsTrue(map.ContainsKey("Foo"));
            Assert.IsNull(BaseOf(map, "Foo"));
        }

        [Test]
        public void ParseInto_MultipleClassesInOneFile_RecordsEachIndependently()
        {
            var map = ParseOnly("public class Foo : Bar { } internal abstract class Baz : Qux { }");

            Assert.AreEqual("Bar", BaseOf(map, "Foo"));
            Assert.AreEqual("Qux", BaseOf(map, "Baz"));
        }

        [Test]
        public void ParseInto_GenericClassDeclaration_BaseStillRecorded()
        {
            var map = ParseOnly("public class Repo<T> : BaseRepo { }");

            Assert.AreEqual("BaseRepo", BaseOf(map, "Repo"));
        }

        [Test]
        public void ParseInto_GenericBaseType_StillResolvesToBaseSimpleName()
        {
            var map = ParseOnly("public class Repo : BaseRepo<Foo> { }");

            Assert.AreEqual("BaseRepo", BaseOf(map, "Repo"));
        }

        [Test]
        public void ParseInto_ModifiersAndAttributesBeforeClass_DoNotConfuseTheMatch()
        {
            var map = ParseOnly(
                "[Serializable]\npublic sealed partial class Foo : Bar\n{\n}\n");

            Assert.AreEqual("Bar", BaseOf(map, "Foo"));
        }

        [Test]
        public void ParseInto_PartialClass_BaseDeclaredInOnlyOnePart_IsPreserved()
        {
            var map = new Dictionary<string, List<ScriptIndex.ClassInfo>>(System.StringComparer.Ordinal);

            // Base-less part scanned first...
            ScriptIndex.ParseInto("public partial class Foo { }", map);
            Assert.IsNull(BaseOf(map, "Foo"));

            // ...then the part with the real base clause must overwrite it.
            ScriptIndex.ParseInto("public partial class Foo : Bar { }", map);
            Assert.AreEqual("Bar", BaseOf(map, "Foo"));
        }

        [Test]
        public void ParseInto_PartialClass_BaseDeclaredFirst_LaterBaselessPartDoesNotBlankIt()
        {
            var map = new Dictionary<string, List<ScriptIndex.ClassInfo>>(System.StringComparer.Ordinal);

            ScriptIndex.ParseInto("public partial class Foo : Bar { }", map);
            ScriptIndex.ParseInto("public partial class Foo { }", map);

            Assert.AreEqual("Bar", BaseOf(map, "Foo"), "A later base-less partial declaration must never overwrite a real base already found.");
        }

        [Test]
        public void ParseInto_EmptyOrNullSource_ProducesNoEntriesAndDoesNotThrow()
        {
            Assert.DoesNotThrow(() => ParseOnly(""));
            Assert.DoesNotThrow(() => ParseOnly(null));
            Assert.IsEmpty(ParseOnly(""));
        }

        [Test]
        public void ParseInto_MalformedGarbageSource_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => ParseOnly("{{{ not even close to C# )()( \0 ￿ class"));
        }

        [Test]
        public void ParseInto_NestedClass_DoesNotCorruptUnrelatedEntries()
        {
            var map = ParseOnly(
                "namespace N { public class Outer : OuterBase { public class Inner : InnerBase { } } }");

            Assert.AreEqual("OuterBase", BaseOf(map, "Outer", "N"));
            Assert.AreEqual("InnerBase", BaseOf(map, "Inner", "N"));
        }

        [Test]
        public void ParseInto_CaseSensitive_DifferentCasingsAreDistinctEntries()
        {
            var map = ParseOnly("public class foo : Bar { } public class Foo : Baz { }");

            Assert.AreEqual("Bar", BaseOf(map, "foo"));
            Assert.AreEqual("Baz", BaseOf(map, "Foo"));
        }

        [Test]
        public void ParseInto_InterfaceOnlyBase_IsRecordedAsIfItWereABaseClass()
        {
            // Known, accepted limitation of a text-scan approach with no semantic type info: a
            // regex can't tell "no base class, just an interface" apart from "base class is X".
            // Pinned here so a future change to this behavior is a deliberate decision, not an
            // accidental regression.
            var map = ParseOnly("public class Foo : IBar { }");

            Assert.AreEqual("IBar", BaseOf(map, "Foo"));
        }

        [Test]
        public void ParseInto_SameShortNameDifferentNamespaces_RecordsBothIndependently()
        {
            var map = ParseOnly(
                "namespace A { public class Foo : BaseA { } } namespace B { public class Foo : BaseB { } }");

            Assert.AreEqual(2, map["Foo"].Count);
            Assert.AreEqual("BaseA", BaseOf(map, "Foo", "A"));
            Assert.AreEqual("BaseB", BaseOf(map, "Foo", "B"));
        }

        private static void Seed(Dictionary<string, List<ScriptIndex.ClassInfo>> map) =>
            PrivateFieldAccess.SetField(typeof(ScriptIndex), "_baseByClass", map);

        private static Dictionary<string, List<ScriptIndex.ClassInfo>> Chain(params (string Name, string Base)[] links)
        {
            var map = new Dictionary<string, List<ScriptIndex.ClassInfo>>(System.StringComparer.Ordinal);
            foreach (var (name, baseName) in links)
                map[name] = new List<ScriptIndex.ClassInfo> { new ScriptIndex.ClassInfo("", baseName) };
            return map;
        }

        [Test]
        public void DerivesFrom_DirectBase_ReturnsTrue()
        {
            Seed(Chain(("A", "B")));

            Assert.IsTrue(ScriptIndex.DerivesFrom("A", "B"));
        }

        [Test]
        public void DerivesFrom_MultiHopChain_ReturnsTrue()
        {
            // A -> B -> C -> TsvrcBehaviour, mirroring InstanceManager -> StateManager -> TsvrcBehaviour.
            Seed(Chain(("A", "B"), ("B", "C"), ("C", "TsvrcBehaviour")));

            Assert.IsTrue(ScriptIndex.DerivesFrom("A", "TsvrcBehaviour"));
        }

        [Test]
        public void DerivesFrom_UnrelatedChain_ReturnsFalse()
        {
            Seed(Chain(("A", "B"), ("B", "MonoBehaviour")));

            Assert.IsFalse(ScriptIndex.DerivesFrom("A", "TsvrcBehaviour"));
        }

        [Test]
        public void DerivesFrom_ClassNotInIndex_ReturnsFalse()
        {
            Seed(Chain());

            Assert.IsFalse(ScriptIndex.DerivesFrom("NeverSeen", "TsvrcBehaviour"));
        }

        [Test]
        public void DerivesFrom_ClassIsTheTargetItself_ReturnsTrue()
        {
            Seed(Chain());

            Assert.IsTrue(ScriptIndex.DerivesFrom("TsvrcBehaviour", "TsvrcBehaviour"));
        }

        [Test]
        public void DerivesFrom_CyclicChain_ReturnsFalseWithoutHanging()
        {
            // Malformed, contrived input, two classes whose recorded bases point at each other,
            // must terminate, not loop forever, and must not report a match for something that
            // isn't actually in the chain.
            Seed(Chain(("A", "B"), ("B", "A")));

            Assert.IsFalse(ScriptIndex.DerivesFrom("A", "TsvrcBehaviour"));
        }

        [Test]
        public void DerivesFrom_SelfInheritance_ReturnsFalseWithoutHanging()
        {
            Seed(Chain(("A", "A")));

            Assert.IsFalse(ScriptIndex.DerivesFrom("A", "TsvrcBehaviour"));
        }

        [Test]
        public void DerivesFrom_NullBaseAtEndOfChain_ReturnsFalse()
        {
            Seed(Chain(("A", "B"), ("B", null)));

            Assert.IsFalse(ScriptIndex.DerivesFrom("A", "TsvrcBehaviour"));
        }

        [Test]
        public void DerivesFrom_AmbiguousShortName_WithMatchingNamespaceHint_ResolvesCorrectly()
        {
            var map = new Dictionary<string, List<ScriptIndex.ClassInfo>>(System.StringComparer.Ordinal)
            {
                ["Foo"] = new List<ScriptIndex.ClassInfo>
                {
                    new ScriptIndex.ClassInfo("A", "TsvrcBehaviour"),
                    new ScriptIndex.ClassInfo("B", "MonoBehaviour"),
                },
            };
            Seed(map);

            Assert.IsTrue(ScriptIndex.DerivesFrom("Foo", "TsvrcBehaviour", "A"));
            Assert.IsFalse(ScriptIndex.DerivesFrom("Foo", "TsvrcBehaviour", "B"));
        }

        [Test]
        public void DerivesFrom_AmbiguousShortName_NoHintResolvesIt_ReturnsFalse()
        {
            // Two unrelated classes named "Foo" in different namespaces, with no namespace hint
            // supplied, must never silently guess based on list or enumeration order.
            var map = new Dictionary<string, List<ScriptIndex.ClassInfo>>(System.StringComparer.Ordinal)
            {
                ["Foo"] = new List<ScriptIndex.ClassInfo>
                {
                    new ScriptIndex.ClassInfo("A", "TsvrcBehaviour"),
                    new ScriptIndex.ClassInfo("B", "MonoBehaviour"),
                },
            };
            Seed(map);

            Assert.IsFalse(ScriptIndex.DerivesFrom("Foo", "TsvrcBehaviour"));
        }

        [Test]
        public void DerivesFrom_AmbiguousShortName_HintMatchesNeither_ReturnsFalse()
        {
            var map = new Dictionary<string, List<ScriptIndex.ClassInfo>>(System.StringComparer.Ordinal)
            {
                ["Foo"] = new List<ScriptIndex.ClassInfo>
                {
                    new ScriptIndex.ClassInfo("A", "TsvrcBehaviour"),
                    new ScriptIndex.ClassInfo("B", "TsvrcBehaviour"),
                },
            };
            Seed(map);

            Assert.IsFalse(ScriptIndex.DerivesFrom("Foo", "TsvrcBehaviour", "C"));
        }

        [Test]
        public void Rebuild_ScansRealProjectMonoScripts_RealFrameworkSubclassResolves()
        {
            // The one code path with otherwise zero coverage is Rebuild()'s AssetDatabase
            // integration, meaning FindAssets, LoadAssetAtPath<MonoScript>, and the null script
            // skip, as opposed to the pure ParseInto and DerivesFrom halves exercised everywhere
            // else via seeded maps. Deliberately exercised against Tsvrc's own real, stable
            // source, Tsvrc.Core.Instance extending TsvrcBehaviour, instead of writing a
            // throwaway .cs file to disk. There is no new asset to create or clean up, and it
            // proves Rebuild() correctly indexes real project files.
            PrivateFieldAccess.SetField(typeof(ScriptIndex), "_baseByClass", null);

            ScriptIndex.Rebuild();

            Assert.IsTrue(ScriptIndex.DerivesFrom("TsvrcBehaviour", "TsvrcBehaviour"));
            Assert.IsTrue(ScriptIndex.DerivesFrom("Instance", "TsvrcBehaviour", "Tsvrc.Core"));
        }
    }
}
