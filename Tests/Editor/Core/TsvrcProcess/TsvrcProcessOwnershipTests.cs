using NUnit.Framework;

namespace Tsvrc.Tests.Editor
{
    public class TsvrcProcessOwnershipTests : TsvrcProcessTestBase
    {
        [Test]
        public void IsProcessOwner_BothIdsZero_ReturnsFalse()
        {
            var process = CreateProcess<TsvrcProcessTestSubclass>();

            Assert.IsFalse(InvokeIsProcessOwner(process));
        }

        [Test]
        public void IsProcessOwner_OwnerZeroLocalNonZero_ReturnsFalse()
        {
            var process = CreateProcess<TsvrcProcessTestSubclass>();
            PrivateFieldAccess.SetField(process, "_localPlayerIdInt", 5);

            Assert.IsFalse(InvokeIsProcessOwner(process));
        }

        [Test]
        public void IsProcessOwner_BothIdsEqualAndNonZero_ReturnsTrue()
        {
            var process = CreateProcess<TsvrcProcessTestSubclass>();
            SeedAsOwner(process);

            Assert.IsTrue(InvokeIsProcessOwner(process));
        }

        [Test]
        public void IsProcessOwner_IdsDifferAndBothNonZero_ReturnsFalse()
        {
            var process = CreateProcess<TsvrcProcessTestSubclass>();
            PrivateFieldAccess.SetField(process, "_localPlayerIdInt", 7);
            PrivateFieldAccess.SetField(process, "_ownerPlayerIdInt", 8);

            Assert.IsFalse(InvokeIsProcessOwner(process));
        }

        [Test]
        public void IsBroadcasting_DefaultsFalse()
        {
            var process = CreateProcess<TsvrcProcessTestSubclass>();

            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(process, "_isBroadcasting"));
        }

        [Test]
        public void LocalPlayerId_DefaultsEmptyBeforeTsStart()
        {
            var process = CreateProcess<TsvrcProcessTestSubclass>();

            Assert.AreEqual("", PrivateFieldAccess.GetField<string>(process, "_localPlayerId"));
        }
    }
}
