using System.Reflection;
using NUnit.Framework;
using Tsvrc.Core;
using Tsvrc.Session;

namespace Tsvrc.Tests.Editor
{
    // Regression guards for parts of RankedGameSession's contract that no behavioral
    // test elsewhere in this suite touches: the nine event string constants (external
    // Udon graphs/behaviours subscribe by literal string via TsSubscribe, not by the
    // C# constant identifier - a silent rename of the underlying value would break
    // them with nothing else here to catch it), the inspector-configured default
    // values of the five [Header("Session")] fields (every behavioral test in this
    // suite explicitly overrides them via SetMasterOnly/SetEndOnTimerComplete/etc.
    // before exercising anything, so the actual field-initializer defaults were never
    // independently confirmed), and that all five [WirePool] sub-behaviour fields are
    // still attributed (the codegen wiring step depends on it).
    public class RankedGameSessionContractRegressionTests : RankedGameSessionTestBase
    {
        [Test]
        public void EventConstants_LiteralStringValues_ArePinned()
        {
            Assert.AreEqual("OnSessionLoading", RankedGameSession.OnSessionLoadingEvent);
            Assert.AreEqual("OnSessionStarted", RankedGameSession.OnSessionStartedEvent);
            Assert.AreEqual("OnSessionStopped", RankedGameSession.OnSessionStoppedEvent);
            Assert.AreEqual("OnSessionEnded", RankedGameSession.OnSessionEndedEvent);
            Assert.AreEqual("OnLobbyPlayerAdded", RankedGameSession.OnLobbyPlayerAddedEvent);
            Assert.AreEqual("OnLobbyPlayerRemoved", RankedGameSession.OnLobbyPlayerRemovedEvent);
            Assert.AreEqual("OnGamePlayerRemoved", RankedGameSession.OnGamePlayerRemovedEvent);
            Assert.AreEqual("OnPlayerCompleted", RankedGameSession.OnPlayerCompletedEvent);
            Assert.AreEqual("OnTimerUpdated", RankedGameSession.OnTimerUpdatedEvent);
        }

        [Test]
        public void StateConstants_ValuesArePinned()
        {
            Assert.AreEqual(0, RankedGameSessionState.Idle);
            Assert.AreEqual(1, RankedGameSessionState.Loading);
            Assert.AreEqual(2, RankedGameSessionState.InGame);
        }

        [Test]
        public void SerializedFieldDefaults_ArePinned()
        {
            // A freshly created component, with none of the harness's SetMasterOnly/
            // SetEndOn*/SetTimerDurationMs helpers ever called - the true C# field
            // initializer defaults, not a test-forced value.
            var session = CreateComponent<RankedGameSessionTestSubclass>();

            Assert.IsTrue(PrivateFieldAccess.GetField<bool>(session, "_masterOnly"));
            Assert.IsTrue(PrivateFieldAccess.GetField<bool>(session, "_endOnTimerComplete"));
            Assert.IsTrue(PrivateFieldAccess.GetField<bool>(session, "_endOnAllGamePlayersLeft"));
            Assert.IsTrue(PrivateFieldAccess.GetField<bool>(session, "_endOnAllPlayersCompleted"));
            Assert.AreEqual(0, PrivateFieldAccess.GetField<int>(session, "_timerDurationMs"));
        }

        [Test]
        public void WirePoolFields_AllFiveStillAttributed()
        {
            // The codegen wiring step (InstanceModule) depends on these five fields
            // staying [WirePool]-attributed to know they need auto-wiring - nothing
            // else in this suite reflects on the attribute itself.
            string[] wirePoolFieldNames = { "_lobbyTracker", "_readyCheck", "_gameTracker", "_completedTracker", "_timer" };

            foreach (string fieldName in wirePoolFieldNames)
            {
                FieldInfo field = typeof(RankedGameSession).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(field, fieldName + " not found on RankedGameSession. Signature changed?");

                var attribute = System.Attribute.GetCustomAttribute(field, typeof(WirePoolAttribute));
                Assert.IsNotNull(attribute, fieldName + " must remain [WirePool] for codegen auto-wiring to find it.");
            }
        }
    }
}
