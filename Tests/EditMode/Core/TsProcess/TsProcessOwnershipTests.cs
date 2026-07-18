using NUnit.Framework;

namespace Tsvrc.Tests.EditMode
{
    public class TsProcessOwnershipTests : TsProcessTestBase
    {
        [Test]
        public void IsProcessOwner_BothIdsZero_ReturnsFalse()
        {
            var process = CreateProcess<TsProcessTestSubclass>();

            Assert.IsFalse(InvokeIsProcessOwner(process));
        }

        [Test]
        public void IsProcessOwner_OwnerZeroLocalNonZero_ReturnsFalse()
        {
            var process = CreateProcess<TsProcessTestSubclass>();
            PrivateFieldAccess.SetField(process, "_localPlayerIdInt", 5);

            Assert.IsFalse(InvokeIsProcessOwner(process));
        }

        [Test]
        public void IsProcessOwner_BothIdsEqualAndNonZero_ReturnsTrue()
        {
            var process = CreateProcess<TsProcessTestSubclass>();
            SeedAsOwner(process);

            Assert.IsTrue(InvokeIsProcessOwner(process));
        }

        [Test]
        public void IsProcessOwner_IdsDifferAndBothNonZero_ReturnsFalse()
        {
            var process = CreateProcess<TsProcessTestSubclass>();
            PrivateFieldAccess.SetField(process, "_localPlayerIdInt", 7);
            PrivateFieldAccess.SetField(process, "_ownerPlayerIdInt", 8);

            Assert.IsFalse(InvokeIsProcessOwner(process));
        }

        [Test]
        public void IsBroadcasting_DefaultsFalse()
        {
            var process = CreateProcess<TsProcessTestSubclass>();

            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(process, "_isBroadcasting"));
        }

        [Test]
        public void LocalPlayerId_DefaultsEmptyBeforeTsStart()
        {
            var process = CreateProcess<TsProcessTestSubclass>();

            Assert.AreEqual("", PrivateFieldAccess.GetField<string>(process, "_localPlayerId"));
        }
    }
}
