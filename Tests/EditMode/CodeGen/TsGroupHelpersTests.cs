using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.EditMode
{
    // TsModule.BreakGroupCycles/BuildGroupPrefix - the shared helpers behind Factory
    // ancestor-chain naming and the group tree's cycle safety, exercised directly via
    // TsModuleTestHarness (see TsModuleHelpersTests) rather than through a real module's
    // LoadConfig(), so each piece is tested independent of TsConfig/TsBuiltinConfig plumbing.
    public class TsGroupHelpersTests
    {
        [Test]
        public void BreakGroupCycles_DirectCycle_ResetsToRootAndLogsError()
        {
            var groupA = new TsGroup { Id = 1, ParentId = 2, Name = "A" };
            var groupB = new TsGroup { Id = 2, ParentId = 1, Name = "B" };
            var groups = new[] { groupA, groupB };

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(@"\[TestModule\] Group cycle detected"));
            TsModuleTestHarness.CallBreakGroupCycles("TestModule", groups);

            Assert.IsTrue(groupA.ParentId == 0 || groupB.ParentId == 0,
                "At least one member of the cycle must be reset to root-level to break it.");
            // Whichever member wasn't reset directly must resolve cleanly now that its ancestor chain terminates.
            Assert.DoesNotThrow(() => TsModuleTestHarness.CallBuildGroupPrefix(groupA.Id, groups, s => s));
            Assert.DoesNotThrow(() => TsModuleTestHarness.CallBuildGroupPrefix(groupB.Id, groups, s => s));
        }

        [Test]
        public void BreakGroupCycles_NoCycle_LeavesParentsUntouched()
        {
            var groups = new[]
            {
                new TsGroup { Id = 1, ParentId = 0, Name = "Root" },
                new TsGroup { Id = 2, ParentId = 1, Name = "Child" },
                new TsGroup { Id = 3, ParentId = 2, Name = "Grandchild" },
            };

            Assert.DoesNotThrow(() => TsModuleTestHarness.CallBreakGroupCycles("TestModule", groups));

            Assert.AreEqual(1, groups[1].ParentId);
            Assert.AreEqual(2, groups[2].ParentId);
        }

        [Test]
        public void BuildGroupPrefix_ThreeLevelChain_ConcatenatesRootToLeaf()
        {
            var groups = new[]
            {
                new TsGroup { Id = 1, ParentId = 0, Name = "Enemies" },
                new TsGroup { Id = 2, ParentId = 1, Name = "Bosses" },
                new TsGroup { Id = 3, ParentId = 2, Name = "Dragons" },
            };

            string prefix = TsModuleTestHarness.CallBuildGroupPrefix(3, groups, s => s);

            Assert.AreEqual("EnemiesBossesDragons", prefix);
        }

        [Test]
        public void BuildGroupPrefix_UngroupedId_ReturnsEmptyString()
        {
            var groups = new[] { new TsGroup { Id = 1, ParentId = 0, Name = "Whatever" } };

            string prefix = TsModuleTestHarness.CallBuildGroupPrefix(0, groups, s => s);

            Assert.AreEqual(string.Empty, prefix);
        }

        [Test]
        public void BuildGroupPrefix_StaleGroupId_ReturnsEmptyStringWithoutThrowing()
        {
            var groups = new[] { new TsGroup { Id = 1, ParentId = 0, Name = "Real" } };

            string prefix = null;
            Assert.DoesNotThrow(() => prefix = TsModuleTestHarness.CallBuildGroupPrefix(999, groups, s => s));
            Assert.AreEqual(string.Empty, prefix);
        }
    }
}
