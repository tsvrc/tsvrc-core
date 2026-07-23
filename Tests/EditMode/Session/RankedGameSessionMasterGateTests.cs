using NUnit.Framework;
using Tsvrc.Session;

namespace Tsvrc.Tests.EditMode
{
    // EditMode migration of Tests/PlayMode/Session/RankedGameSessionPlayModeTests.cs.
    // TsInstance.IsTsMaster was already virtual (no Runtime change
    // needed here) - TsInstanceTestSubclass overrides it directly, so the master-gate's DENIED
    // branch (a real, non-master ClientSim local player in the original) is reachable here via
    // a simulated non-master TsInstance instead. The existing EditMode suite
    // (RankedGameSessionStartStopTests.StartSession_MasterOnlyTrueWithRealTsInstance_DefaultIsTsMasterTrue_Starts)
    // already covered the ALLOWED branch with a real, unsubclassed TsInstance defaulting to
    // master with no session at all; this file adds the DENIED branch alongside it.
    public class RankedGameSessionMasterGateTests : RankedGameSessionTestBase
    {
        [Test]
        public void StartSession_MasterOnlyTrueNonMasterInstance_WarnsAndDoesNotStart()
        {
            var instance = CreateComponent<TsInstanceTestSubclass>();
            instance.SimulatedIsTsMaster = false;
            var root = new InstanceOnlyTsRootDouble { FakeInstance = instance };
            // _masterOnly defaults to true (RankedGameSession's own [SerializeField] default);
            // CreateWiredSession(root) - unlike the 0-arg overload - does not force it false.
            var h = CreateWiredSession(root);
            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });

            h.Session.StartSession();

            Assert.IsFalse(instance.IsTsMaster);
            Assert.AreEqual(RankedGameSessionState.Idle, h.Session.CurrentState);
        }
    }
}
