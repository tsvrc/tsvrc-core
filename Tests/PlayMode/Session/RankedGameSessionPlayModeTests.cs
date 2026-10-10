using System.Collections;
using NUnit.Framework;
using Tsvrc.Tests.EditMode;
using Tsvrc.Tests.PlayMode.Core.Process;
using Tsvrc.UdonTestKit;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDKBase;

namespace Tsvrc.Tests.PlayMode.Session
{
    // RankedGameSession has no direct real-VRC-API surface of its own beyond composing five
    // already-covered Process-derived sub-components (three PlayerTrackers, a
    // ReadyCheckProcess, a TsvrcTimer). This is an end-to-end integration smoke test with real
    // spawned players flowing through the full lobby -> ready-check -> game -> completed
    // pipeline, wiring the sub-components into the session's private fields via reflection to
    // reproduce what [WirePool] wires at runtime.
    public class RankedGameSessionPlayModeTests : ProcessPlayModeTestBase
    {
        [UnityTest]
        public IEnumerator FullLifecycle_RealPlayersFlowThroughLobbyReadyGameCompleted_EndsSessionNaturally()
        {
            yield return Session.Start();
            VRCPlayerApi p1 = Session.SpawnRemotePlayer("P1");
            VRCPlayerApi p2 = Session.SpawnRemotePlayer("P2");
            string p1Id = p1.displayName + "#" + p1.playerId;
            string p2Id = p2.displayName + "#" + p2.playerId;

            var session = new GameObject(nameof(RankedGameSessionPlayModeTests)).AddComponent<RankedGameSessionTestSubclass>();
            var lobbyTracker = CreateProcess<PlayerTrackerTestSubclass>();
            var readyCheck = CreateProcess<ReadyCheckProcessTestSubclass>();
            var gameTracker = CreateProcess<PlayerTrackerTestSubclass>();
            var completedTracker = CreateProcess<PlayerTrackerTestSubclass>();
            var timer = CreateProcess<TsTimerTestSubclass>();
            lobbyTracker.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            readyCheck.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            gameTracker.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            completedTracker.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);
            timer.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);

            PrivateFieldAccess.SetField(session, "_lobbyTracker", lobbyTracker);
            PrivateFieldAccess.SetField(session, "_readyCheck", readyCheck);
            PrivateFieldAccess.SetField(session, "_gameTracker", gameTracker);
            PrivateFieldAccess.SetField(session, "_completedTracker", completedTracker);
            PrivateFieldAccess.SetField(session, "_timer", timer);

            session.TsConstruct((Tsvrc.Core.Generated.TsRoot)null);

            session.StartLobbyTracking();
            session.AddLobbyPlayer(p1Id);
            session.AddLobbyPlayer(p2Id);
            session.StartSession();

            Assert.AreEqual(Tsvrc.Session.RankedGameSessionState.Loading, session.CurrentState);

            readyCheck.BroadcastAddReadyPlayer(p1Id);
            readyCheck.BroadcastAddReadyPlayer(p2Id);

            Assert.AreEqual(Tsvrc.Session.RankedGameSessionState.InGame, session.CurrentState,
                "Both real lobby players marking ready must move the session into the game state.");
            Assert.AreEqual(2, session.GamePlayerIds.Length);

            session.AddCompletedPlayer(p1Id);
            session.AddCompletedPlayer(p2Id);

            Assert.AreEqual(Tsvrc.Session.RankedGameSessionState.Idle, session.CurrentState,
                "Every real game player completing must end the session naturally.");
            Assert.AreEqual(1, session.OnSessionEndedCount);
        }
    }
}
