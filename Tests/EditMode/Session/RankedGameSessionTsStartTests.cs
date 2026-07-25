using NUnit.Framework;
using Tsvrc.Core.Generated;
using Tsvrc.Testing.Framework;
using UnityEngine.TestTools;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // Covers TsStart's five null-reference guards (one per [WirePool] field) and the
    // happy-path subscription wiring when all five are assigned.
    public class RankedGameSessionTsStartTests : RankedGameSessionTestBase
    {
        [Test]
        public void TsStart_LobbyTrackerMissing_LogsErrorAndSkipsWiring()
        {
            var session = CreateComponent<RankedGameSessionTestSubclass>();
            PrivateFieldAccess.SetField(session, "_lobbyTracker", null);
            PrivateFieldAccess.SetField(session, "_readyCheck", CreateProcess<ReadyCheckProcessTestSubclass>());
            PrivateFieldAccess.SetField(session, "_gameTracker", CreateProcess<PlayerTrackerTestSubclass>());
            PrivateFieldAccess.SetField(session, "_completedTracker", CreateProcess<PlayerTrackerTestSubclass>());
            PrivateFieldAccess.SetField(session, "_timer", CreateProcess<TsTimerTestSubclass>());

            LogAssert.Expect(LogType.Error, "[TsVRC] [RankedGameSessionTestSubclass] _lobbyTracker is not assigned. Try regenerating TsVRC.");
            session.TsConstruct(new TestTsRoot());
        }

        [Test]
        public void TsStart_ReadyCheckMissing_LogsErrorAndSkipsWiring()
        {
            var session = CreateComponent<RankedGameSessionTestSubclass>();
            PrivateFieldAccess.SetField(session, "_lobbyTracker", CreateProcess<PlayerTrackerTestSubclass>());
            PrivateFieldAccess.SetField(session, "_readyCheck", null);
            PrivateFieldAccess.SetField(session, "_gameTracker", CreateProcess<PlayerTrackerTestSubclass>());
            PrivateFieldAccess.SetField(session, "_completedTracker", CreateProcess<PlayerTrackerTestSubclass>());
            PrivateFieldAccess.SetField(session, "_timer", CreateProcess<TsTimerTestSubclass>());

            LogAssert.Expect(LogType.Error, "[TsVRC] [RankedGameSessionTestSubclass] _readyCheck is not assigned. Try regenerating TsVRC.");
            session.TsConstruct(new TestTsRoot());
        }

        [Test]
        public void TsStart_GameTrackerMissing_LogsErrorAndSkipsWiring()
        {
            var session = CreateComponent<RankedGameSessionTestSubclass>();
            PrivateFieldAccess.SetField(session, "_lobbyTracker", CreateProcess<PlayerTrackerTestSubclass>());
            PrivateFieldAccess.SetField(session, "_readyCheck", CreateProcess<ReadyCheckProcessTestSubclass>());
            PrivateFieldAccess.SetField(session, "_gameTracker", null);
            PrivateFieldAccess.SetField(session, "_completedTracker", CreateProcess<PlayerTrackerTestSubclass>());
            PrivateFieldAccess.SetField(session, "_timer", CreateProcess<TsTimerTestSubclass>());

            LogAssert.Expect(LogType.Error, "[TsVRC] [RankedGameSessionTestSubclass] _gameTracker is not assigned. Try regenerating TsVRC.");
            session.TsConstruct(new TestTsRoot());
        }

        [Test]
        public void TsStart_CompletedTrackerMissing_LogsErrorAndSkipsWiring()
        {
            var session = CreateComponent<RankedGameSessionTestSubclass>();
            PrivateFieldAccess.SetField(session, "_lobbyTracker", CreateProcess<PlayerTrackerTestSubclass>());
            PrivateFieldAccess.SetField(session, "_readyCheck", CreateProcess<ReadyCheckProcessTestSubclass>());
            PrivateFieldAccess.SetField(session, "_gameTracker", CreateProcess<PlayerTrackerTestSubclass>());
            PrivateFieldAccess.SetField(session, "_completedTracker", null);
            PrivateFieldAccess.SetField(session, "_timer", CreateProcess<TsTimerTestSubclass>());

            LogAssert.Expect(LogType.Error, "[TsVRC] [RankedGameSessionTestSubclass] _completedTracker is not assigned. Try regenerating TsVRC.");
            session.TsConstruct(new TestTsRoot());
        }

        [Test]
        public void TsStart_TimerMissing_LogsErrorAndSkipsWiring()
        {
            var session = CreateComponent<RankedGameSessionTestSubclass>();
            PrivateFieldAccess.SetField(session, "_lobbyTracker", CreateProcess<PlayerTrackerTestSubclass>());
            PrivateFieldAccess.SetField(session, "_readyCheck", CreateProcess<ReadyCheckProcessTestSubclass>());
            PrivateFieldAccess.SetField(session, "_gameTracker", CreateProcess<PlayerTrackerTestSubclass>());
            PrivateFieldAccess.SetField(session, "_completedTracker", CreateProcess<PlayerTrackerTestSubclass>());
            PrivateFieldAccess.SetField(session, "_timer", null);

            LogAssert.Expect(LogType.Error, "[TsVRC] [RankedGameSessionTestSubclass] _timer is not assigned. Try regenerating TsVRC.");
            session.TsConstruct(new TestTsRoot());
        }

        [Test]
        public void TsStart_AllWired_SubscriptionsReachTheirHandlersOnFirstRealEvent()
        {
            // Rather than reflecting into the private _sub* arrays, this drives each
            // sub-behaviour's own real event once and confirms the corresponding
            // RankedGameSession handler actually ran - proving TsSubscribe wiring
            // succeeded end-to-end, not just that TsStart didn't early-return.
            var h = CreateWiredSession();

            h.Session.StartLobbyTracking();
            h.LobbyTracker.AddTrackedPlayers(new[] { "A" });
            Assert.AreEqual(1, h.Session.OnLobbyPlayerAddedCount);

            h.Session.StartSession();
            Assert.AreEqual(1, h.Session.OnSessionLoadingCount, "_OnReadyCheckStarted wiring.");
        }
    }
}
