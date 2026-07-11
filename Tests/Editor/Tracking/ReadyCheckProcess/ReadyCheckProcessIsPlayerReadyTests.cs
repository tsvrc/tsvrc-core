using NUnit.Framework;

namespace Tsvrc.Tests.Editor
{
    // Dedicated, direct coverage of IsPlayerReady's own contract - elsewhere in the suite
    // it's only ever exercised indirectly as an implementation detail of SetReady/
    // BroadcastAddReadyPlayer/BroadcastRemoveReadyPlayer/CheckAllPlayersReady, mirroring
    // PlayerTrackerIsTrackedPlayerTests's dedicated treatment of IsTrackedPlayer.
    public class ReadyCheckProcessIsPlayerReadyTests : ReadyCheckProcessTestBase
    {
        [Test]
        public void IsPlayerReady_IdInReadySet_ReturnsTrue()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SetReadyPlayerIds(tracker, new[] { "A", "B" });

            Assert.IsTrue(InvokeIsPlayerReady(tracker, "A"));
            Assert.IsTrue(InvokeIsPlayerReady(tracker, "B"));
        }

        [Test]
        public void IsPlayerReady_IdNotInReadySet_ReturnsFalse()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SetReadyPlayerIds(tracker, new[] { "A" });

            Assert.IsFalse(InvokeIsPlayerReady(tracker, "Z"));
        }

        [Test]
        public void IsPlayerReady_EmptyReadySet_ReturnsFalse()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SetReadyPlayerIds(tracker, new string[0]);

            Assert.IsFalse(InvokeIsPlayerReady(tracker, "A"));
        }

        [Test]
        public void IsPlayerReady_NullId_ReturnsFalse()
        {
            var tracker = CreateProcess<ReadyCheckProcessTestSubclass>();
            SetReadyPlayerIds(tracker, new[] { "A" });

            Assert.IsFalse(InvokeIsPlayerReady(tracker, null));
        }
    }
}
