using NUnit.Framework;
using Tsvrc.Testing.Framework;

namespace Tsvrc.Tests.EditMode
{
    public class TsProcessIntegrationTests : TsProcessTestBase
    {
        [Test]
        public void GoldenPath_StartWithUpdatesThenComplete_FullHookOrderAndFinalState()
        {
            var process = CreateProcess<TsProcessTestSubclass>();
            SeedAsOwner(process);

            process.StartProcess(useProcessUpdate: true);
            process._TickProcessUpdate();
            ForceNextTickDueNow(process);
            process._TickProcessUpdate();
            ForceNextTickDueNow(process);
            process._TickProcessUpdate();
            process.CompleteProcess();

            CollectionAssert.AreEqual(
                new[]
                {
                    "OnProcessStarted",
                    "OnProcessUpdate",
                    "OnProcessUpdate",
                    "OnProcessUpdate",
                    "OnProcessCompleted",
                    "OnProcessCleanup:True",
                },
                process.CallLog);
            Assert.IsFalse(process.IsProcessRunning());
            Assert.AreEqual("", PrivateFieldAccess.GetField<string>(process, "_ownerId"));
        }

        [Test]
        public void GoldenPath_StartThenStopBeforeCompletion_OnlyStoppedHookFires()
        {
            var process = CreateProcess<TsProcessTestSubclass>();
            SeedAsOwner(process);

            process.StartProcess();
            process.StopProcess();

            Assert.AreEqual(1, process.OnProcessStoppedCount);
            Assert.AreEqual(0, process.OnProcessCompletedCount);
            CollectionAssert.AreEqual(
                new[] { "OnProcessStarted", "OnProcessStopped", "OnProcessCleanup:False" },
                process.CallLog);
        }

        [Test]
        public void TwoIndependentClients_BothStartProcessBeforeSyncArrives_LaterPacketOverwritesLoser()
        {
            // Two independent TsProcess C# instances stand in for two real clients
            // — each is just an object with its own fields. Packet delivery is
            // simulated by copying the [UdonSynced] fields from the "winning"
            // instance onto the other, exactly what a real deserialization packet
            // would overwrite.
            var clientA = CreateProcess<TsProcessTestSubclass>("ClientA");
            var clientB = CreateProcess<TsProcessTestSubclass>("ClientB");
            SeedAsOwner(clientA, 100);
            SeedAsOwner(clientB, 200);

            // The race: both clients call StartProcess() while each still sees the
            // object as not-running (only "who owns it" was seeded, not _isRunning) —
            // neither has received the other's packet yet.
            clientA.StartProcess();
            clientB.StartProcess();

            Assert.IsTrue(clientA.IsProcessRunning());
            Assert.IsTrue(clientB.IsProcessRunning());
            Assert.AreEqual(1, clientA.OnProcessStartedCount);
            Assert.AreEqual(1, clientB.OnProcessStartedCount);
            Assert.IsTrue((bool)PrivateFieldAccess.InvokeInstance(clientA, "IsProcessOwner"),
                "Both clients pass the _isRunning guard and both fire OnProcessStarted.");
            Assert.IsTrue((bool)PrivateFieldAccess.InvokeInstance(clientB, "IsProcessOwner"));

            // Simulate A's packet (the eventual network authority) finally arriving
            // at B, overwriting B's local synced state exactly as OnDeserialization's
            // incoming packet would.
            PrivateFieldAccess.SetField(clientB, "_isRunning", PrivateFieldAccess.GetField<bool>(clientA, "_isRunning"));
            PrivateFieldAccess.SetField(clientB, "_ownerId", PrivateFieldAccess.GetField<string>(clientA, "_ownerId"));
            PrivateFieldAccess.SetField(clientB, "_ownerPlayerIdInt", PrivateFieldAccess.GetField<int>(clientA, "_ownerPlayerIdInt"));
            PrivateFieldAccess.SetField(clientB, "_useProcessUpdate", PrivateFieldAccess.GetField<bool>(clientA, "_useProcessUpdate"));

            // _isRunning still says true (A's packet says so too), but B is no longer the owner.
            Assert.IsTrue(clientB.IsProcessRunning(),
                "B still locally believes the process is running — the synced flag from A's packet says so too.");
            Assert.IsFalse((bool)PrivateFieldAccess.InvokeInstance(clientB, "IsProcessOwner"),
                "B lost the ownership race once A's packet overwrote its local state.");
            Assert.IsTrue((bool)PrivateFieldAccess.InvokeInstance(clientA, "IsProcessOwner"),
                "A's own state is unaffected by B's now-discarded packet.");
        }

        [Test]
        public void TsProcessSubclass_CanUseInheritedPubSub_FromWithinItsOwnHook()
        {
            var process = CreateProcess<EventingProcess>();
            SeedAsOwner(process);
            var listenerDouble = CreateComponent<TsListenerDouble>("Listener");
            process.TsSubscribe(listenerDouble, EventingProcess.DoneEvent, nameof(TsListenerDouble.CallbackA));

            process.StartProcess();
            process.CompleteProcess();

            Assert.AreEqual(1, listenerDouble.CallbackACount);
        }
    }
}
