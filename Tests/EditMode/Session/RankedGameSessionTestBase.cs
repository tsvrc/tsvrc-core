using Tsvrc.Core.Generated;
using Tsvrc.Core;
using Tsvrc.Testing.Framework;

namespace Tsvrc.Tests.EditMode
{
    // RankedGameSession composes five separate sub-behaviours (three PlayerTrackers, a
    // ReadyCheckProcess, a TsvrcTimer) rather than inheriting from Process itself,
    // so this base extends ProcessTestBase directly for CreateComponent<T>/
    // CreateProcess<T>/SeedAsOwner/ForceNextTickDueNow/TearDown, then wires all five
    // sub-components into the session's private fields via reflection - the same
    // hierarchy TsGenerator's InstanceModule would wire at runtime through
    // [WirePool], reproduced here without depending on the generator.
    public abstract class RankedGameSessionTestBase : ProcessTestBase
    {
        protected class Harness
        {
            public RankedGameSessionTestSubclass Session;
            public PlayerTrackerTestSubclass LobbyTracker;
            public ReadyCheckProcessTestSubclass ReadyCheck;
            public PlayerTrackerTestSubclass GameTracker;
            public PlayerTrackerTestSubclass CompletedTracker;
            public TsTimerTestSubclass Timer;
        }

        // Builds a fully-wired session with a plain TsRoot double.
        protected Harness CreateWiredSession() => CreateWiredSession(null);

        // Same wiring, with a hook to run just before TsConstruct (and therefore before
        // RankedGameSession.TsStart) - lets a test seed a sub-tracker as already running, to
        // simulate a client joining mid-session, before CurrentState's one-time catch-up read.
        protected Harness CreateWiredSession(System.Action<Harness> beforeConstruct)
        {
            var h = new Harness
            {
                Session = CreateComponent<RankedGameSessionTestSubclass>(),
                LobbyTracker = CreateProcess<PlayerTrackerTestSubclass>(),
                ReadyCheck = CreateProcess<ReadyCheckProcessTestSubclass>(),
                GameTracker = CreateProcess<PlayerTrackerTestSubclass>(),
                CompletedTracker = CreateProcess<PlayerTrackerTestSubclass>(),
                Timer = CreateProcess<TsTimerTestSubclass>(),
            };

            SeedAsOwner(h.LobbyTracker);
            SeedAsOwner(h.ReadyCheck);
            SeedAsOwner(h.GameTracker);
            SeedAsOwner(h.CompletedTracker);
            SeedAsOwner(h.Timer);

            PrivateFieldAccess.SetField(h.Session, "_lobbyTracker", h.LobbyTracker);
            PrivateFieldAccess.SetField(h.Session, "_readyCheck", h.ReadyCheck);
            PrivateFieldAccess.SetField(h.Session, "_gameTracker", h.GameTracker);
            PrivateFieldAccess.SetField(h.Session, "_completedTracker", h.CompletedTracker);
            PrivateFieldAccess.SetField(h.Session, "_timer", h.Timer);

            beforeConstruct?.Invoke(h);

            h.Session.TsConstruct(new TestTsRoot());

            return h;
        }

        protected static void SetEndOnTimerComplete(RankedGameSessionTestSubclass s, bool v) =>
            PrivateFieldAccess.SetField(s, "_endOnTimerComplete", v);

        protected static void SetEndOnAllGamePlayersLeft(RankedGameSessionTestSubclass s, bool v) =>
            PrivateFieldAccess.SetField(s, "_endOnAllGamePlayersLeft", v);

        protected static void SetEndOnAllPlayersCompleted(RankedGameSessionTestSubclass s, bool v) =>
            PrivateFieldAccess.SetField(s, "_endOnAllPlayersCompleted", v);

        protected static void SetStopOnEmptyLobbyDuringLoading(RankedGameSessionTestSubclass s, bool v) =>
            PrivateFieldAccess.SetField(s, "_stopOnEmptyLobbyDuringLoading", v);

        protected static void SetTimerDurationMsField(RankedGameSessionTestSubclass s, int v) =>
            PrivateFieldAccess.SetField(s, "_timerDurationMs", v);

        protected static int GetTimerDurationMsField(RankedGameSessionTestSubclass s) =>
            PrivateFieldAccess.GetField<int>(s, "_timerDurationMs");
    }
}
