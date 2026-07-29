using NUnit.Framework;
using Tsvrc.Testing.Framework;

namespace Tsvrc.Tests.EditMode
{
    public class ProcessOwnershipTests : ProcessTestBase
    {
        [Test]
        public void IsProcessOwner_BothIdsZero_ReturnsFalse()
        {
            var process = CreateProcess<ProcessTestSubclass>();

            Assert.IsFalse(InvokeIsProcessOwner(process));
        }

        [Test]
        public void IsProcessOwner_OwnerZeroLocalNonZero_ReturnsFalse()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            PrivateFieldAccess.SetField(process, "_localPlayerIdInt", 5);

            Assert.IsFalse(InvokeIsProcessOwner(process));
        }

        [Test]
        public void IsProcessOwner_BothIdsEqualAndNonZero_ReturnsTrue()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            SeedAsOwner(process);

            Assert.IsTrue(InvokeIsProcessOwner(process));
        }

        [Test]
        public void IsProcessOwner_IdsDifferAndBothNonZero_ReturnsFalse()
        {
            var process = CreateProcess<ProcessTestSubclass>();
            PrivateFieldAccess.SetField(process, "_localPlayerIdInt", 7);
            PrivateFieldAccess.SetField(process, "_ownerPlayerIdInt", 8);

            Assert.IsFalse(InvokeIsProcessOwner(process));
        }

        [Test]
        public void IsBroadcasting_DefaultsFalse()
        {
            var process = CreateProcess<ProcessTestSubclass>();

            Assert.IsFalse(PrivateFieldAccess.GetField<bool>(process, "_isBroadcasting"));
        }

        [Test]
        public void LocalPlayerId_DefaultsEmptyBeforeTsStart()
        {
            var process = CreateProcess<ProcessTestSubclass>();

            Assert.AreEqual("", PrivateFieldAccess.GetField<string>(process, "_localPlayerId"));
        }
    }
}
