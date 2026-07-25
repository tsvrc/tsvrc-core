using Tsvrc.Core.Generated;
using Tsvrc.Core;
using Tsvrc.Testing.Framework;

namespace Tsvrc.Tests.EditMode
{
    // RankedGameSession composes five separate sub-behaviours (three PlayerTrackers, a
    // ReadyCheckProcess, a TsTimer) rather than inheriting from TsProcess itself,
    // so this base extends TsProcessTestBase directly for CreateComponent<T>/
    // CreateProcess<T>/SeedAsOwner/ForceNextTickDueNow/TearDown, then wires all five
    // sub-components into the session's private fields via reflection - the same
    // hierarchy TsGenerator's InstanceModule would wire at runtime through
    // [WirePool], reproduced here without depending on the generator.
    public abstract class RankedGameSessionTestBase : TsProcessTestBase
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

        // Builds a fully-wired session with a TsRoot double that has no overridden
        // Instance, and _masterOnly forced false so _ts.Instance.IsTsMaster is never
        // dereferenced (Instance is null on this root - only tests of the master gate
        // itself need a real, non-null TsInstance; see CreateWiredSession(TsRoot)).
        protected Harness CreateWiredSession()
        {
            var h = CreateWiredSession(new TestTsRoot());
            SetMasterOnly(h.Session, false);
            return h;
        }

        protected Harness CreateWiredSession(TsRoot root)
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

            h.Session.TsConstruct(root);

            return h;
        }

        protected static void SetMasterOnly(RankedGameSessionTestSubclass s, bool v) =>
            PrivateFieldAccess.SetField(s, "_masterOnly", v);

        protected static void SetEndOnTimerComplete(RankedGameSessionTestSubclass s, bool v) =>
            PrivateFieldAccess.SetField(s, "_endOnTimerComplete", v);

        protected static void SetEndOnAllGamePlayersLeft(RankedGameSessionTestSubclass s, bool v) =>
            PrivateFieldAccess.SetField(s, "_endOnAllGamePlayersLeft", v);

        protected static void SetEndOnAllPlayersCompleted(RankedGameSessionTestSubclass s, bool v) =>
            PrivateFieldAccess.SetField(s, "_endOnAllPlayersCompleted", v);

        protected static void SetTimerDurationMsField(RankedGameSessionTestSubclass s, int v) =>
            PrivateFieldAccess.SetField(s, "_timerDurationMs", v);

        protected static int GetTimerDurationMsField(RankedGameSessionTestSubclass s) =>
            PrivateFieldAccess.GetField<int>(s, "_timerDurationMs");
    }
}
