using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Tsvrc.Core;

namespace Tsvrc.Tests.EditMode
{
    public class TsWorldExtensionPointAttributeTests
    {
        [TsWorldExtensionPoint("TaggedDummyShadow")]
        private class TaggedDummy
        {
        }

        private class UntaggedDerivedFromTaggedDummy : TaggedDummy
        {
        }

        [Test]
        public void AttributeUsage_TargetsClassesOnly_AndIsNotInherited()
        {
            var usage = (System.AttributeUsageAttribute)System.Attribute.GetCustomAttribute(
                typeof(TsWorldExtensionPointAttribute), typeof(System.AttributeUsageAttribute));

            Assert.IsNotNull(usage);
            Assert.AreEqual(System.AttributeTargets.Class, usage.ValidOn);
            Assert.IsFalse(usage.Inherited,
                "Inherited must stay false: ScaffoldModule.DiscoverWorldBaseTypes reflects with inherit:false " +
                "so a subclass of a tagged framework class (e.g. a project's own intermediate abstract class) " +
                "never silently becomes a second, redundant shadow target.");
        }

        [Test]
        public void Constructor_SetsGeneratedName()
        {
            var attr = new TsWorldExtensionPointAttribute("TsBehaviour");

            Assert.AreEqual("TsBehaviour", attr.GeneratedName);
        }

        [Test]
        public void IsDefined_WithInheritFalse_DoesNotPropagateToSubclasses()
        {
            // Mirrors exactly how ScaffoldModule.DiscoverWorldBaseTypes queries this attribute,
            // so this is the actual mechanism, not just a metadata check above.
            Assert.IsTrue(typeof(TaggedDummy).IsDefined(typeof(TsWorldExtensionPointAttribute), inherit: false));
            Assert.IsFalse(typeof(UntaggedDerivedFromTaggedDummy).IsDefined(typeof(TsWorldExtensionPointAttribute), inherit: false));
        }

        [Test]
        public void TsvrcRuntimeAssembly_TagsExactlyTheKnownExtensionPoints_WithTheirGeneratedNames()
        {
            // Locks in which Tsvrc framework classes world scripts may subclass directly today,
            // and the exact shadow name ScaffoldModule generates for each. Any class not listed
            // here is deliberately internal-only plumbing (e.g. the DataTransferer chain below
            // DataTransferer itself); tagging it would make ScaffoldModule generate an unused
            // shadow class for it.
            var tagged = typeof(TsvrcBehaviour).Assembly.GetTypes()
                .Where(t => t.IsClass && t.IsDefined(typeof(TsWorldExtensionPointAttribute), inherit: false))
                .Select(t => (InternalName: t.Name, GeneratedName: t.GetCustomAttribute<TsWorldExtensionPointAttribute>().GeneratedName))
                .OrderBy(x => x.InternalName)
                .ToArray();

            var expected = new[]
            {
                (InternalName: "AutoPlayerTracker", GeneratedName: "TsAutoPlayerTracker"),
                (InternalName: "DataTransferer", GeneratedName: "TsDataTransferer"),
                (InternalName: "HeadClipGuard", GeneratedName: "TsHeadClipGuard"),
                (InternalName: "Instance", GeneratedName: "TsInstance"),
                (InternalName: "ListItem", GeneratedName: "TsListItem"),
                (InternalName: "PlayerPositionOverlay", GeneratedName: "TsPlayerPositionOverlay"),
                (InternalName: "PlayerTracker", GeneratedName: "TsPlayerTracker"),
                (InternalName: "Process", GeneratedName: "TsProcess"),
                (InternalName: "RankedGameSession", GeneratedName: "TsRankedGameSession"),
                (InternalName: "ReadyCheckProcess", GeneratedName: "TsReadyCheckProcess"),
                (InternalName: "StateManager", GeneratedName: "TsStateManager"),
                (InternalName: "TsvrcBehaviour", GeneratedName: "TsBehaviour"),
                (InternalName: "TsvrcList", GeneratedName: "TsList"),
                (InternalName: "TsvrcLogger", GeneratedName: "TsLogger"),
                (InternalName: "TsvrcMemory", GeneratedName: "TsMemory"),
                (InternalName: "TsvrcTimer", GeneratedName: "TsTimer"),
            };

            CollectionAssert.AreEquivalent(expected, tagged);
        }
    }
}
