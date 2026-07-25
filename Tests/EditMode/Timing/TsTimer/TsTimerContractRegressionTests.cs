using NUnit.Framework;
using System.Reflection;
using Tsvrc.Testing.Framework;
using Tsvrc.Timing;

namespace Tsvrc.Tests.EditMode
{
    // Regression guards for two parts of TsTimer's public contract that no
    // behavioral test elsewhere in this suite touches, because both are about the
    // exact surface external consumers depend on rather than runtime logic: the
    // seven event string constants (external Udon graphs/behaviours subscribe by
    // literal string via TsSubscribe, not by the C# constant identifier - a silent
    // rename of the underlying value would break them with nothing else here to
    // catch it) and the [NetworkCallable(maxEventsPerSecond: 2)] rate limit on
    // RequestPauseTimer/RequestResumeTimer (their guard *logic* is already covered by
    // TsTimerPauseResumeTests.cs - this is only about the attribute metadata itself
    // still being present and unchanged).
    public class TsTimerContractRegressionTests : TsTimerTestBase
    {
        [Test]
        public void EventConstants_LiteralStringValues_ArePinned()
        {
            Assert.AreEqual("OnTimerStarted", TsTimer.OnTimerStartedEvent);
            Assert.AreEqual("OnTimerStopped", TsTimer.OnTimerStoppedEvent);
            Assert.AreEqual("OnTimerCompleted", TsTimer.OnTimerCompletedEvent);
            Assert.AreEqual("OnTimerPaused", TsTimer.OnTimerPausedEvent);
            Assert.AreEqual("OnTimerResumed", TsTimer.OnTimerResumedEvent);
            Assert.AreEqual("OnTimerUpdated", TsTimer.OnTimerUpdatedEvent);
            Assert.AreEqual("OnTimerDeserialization", TsTimer.OnTimerDeserializationEvent);
        }

        // Checked by attribute type NAME via plain System.Attribute reflection rather
        // than importing VRC.SDK3.UdonNetworkCalling directly - that namespace's
        // assembly isn't referenced by the Tsvrc.Tests.EditMode asmdef (only by
        // Tsvrc.Runtime, which declares the attribute usage but doesn't re-export the
        // reference transitively for this purpose), and adding a new asmdef reference
        // is a bigger, riskier change than this single regression guard warrants.
        private static void AssertHasNetworkCallableRateLimit(string methodName, int expectedMaxEventsPerSecond)
        {
            MethodInfo method = typeof(TsTimer).GetMethod(methodName);
            Assert.IsNotNull(method, methodName + " not found on TsTimer. Signature changed?");

            object[] attributes = method.GetCustomAttributes(false);
            object networkCallable = null;
            foreach (object attr in attributes)
            {
                if (attr.GetType().Name == "NetworkCallableAttribute")
                {
                    networkCallable = attr;
                    break;
                }
            }

            Assert.IsNotNull(networkCallable, methodName + " must remain [NetworkCallable] so non-owner calls can reach the owner.");

            // Exact member name/casing/field-vs-property shape isn't assumed - search
            // every public instance field and property case-insensitively for
            // "maxeventspersecond", since the whole point of not importing the type
            // directly (see comment above) is to not couple this regression guard to
            // that installed-SDK-version detail either.
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            object rateLimitValue = null;
            bool found = false;

            foreach (FieldInfo field in networkCallable.GetType().GetFields(flags))
            {
                if (field.Name.ToLowerInvariant() == "maxeventspersecond")
                {
                    rateLimitValue = field.GetValue(networkCallable);
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                foreach (PropertyInfo property in networkCallable.GetType().GetProperties(flags))
                {
                    if (property.Name.ToLowerInvariant() == "maxeventspersecond")
                    {
                        rateLimitValue = property.GetValue(networkCallable);
                        found = true;
                        break;
                    }
                }
            }

            Assert.IsTrue(found, "NetworkCallableAttribute's rate-limit member not found by name on either fields or properties. Signature changed?");
            Assert.AreEqual(expectedMaxEventsPerSecond, rateLimitValue);
        }

        [Test]
        public void RequestPauseTimer_NetworkCallableAttribute_RateLimitIsPinnedAtTwoPerSecond()
        {
            AssertHasNetworkCallableRateLimit(nameof(TsTimer.RequestPauseTimer), 2);
        }

        [Test]
        public void RequestResumeTimer_NetworkCallableAttribute_RateLimitIsPinnedAtTwoPerSecond()
        {
            AssertHasNetworkCallableRateLimit(nameof(TsTimer.RequestResumeTimer), 2);
        }

        // Mirrors TsInstanceTests.TsStart_IsNotOverriddenByTsInstance's own
        // reflection-based "prove a non-override, not just assume it" pattern.
        [Test]
        public void DoesNotOverride_OnOwnerAbandonedProcess()
        {
            MethodInfo method = typeof(TsTimer).GetMethod("OnOwnerAbandonedProcess",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method);
            Assert.AreEqual(typeof(Tsvrc.Core.TsProcess), method.DeclaringType,
                "TsTimer must not override OnOwnerAbandonedProcess - if it ever does, the " +
                "ownership-handover Play Mode tests' premise (no timer-specific override to break) " +
                "silently stops holding.");
        }

        // OnPlayerLeft/OnOwnershipTransferred/OnPlayerSuspendChanged are each
        // overloaded (UdonSharpBehaviour's own base declarations plus TsProcess's
        // override), so plain GetMethod(name) throws AmbiguousMatchException - filtered
        // by parameter count instead of naming VRCPlayerApi as the parameter type
        // directly, since that type isn't resolvable from the Tsvrc.Tests.EditMode
        // asmdef at all (established convention in this suite - see
        // TsProcessTestBase's own header comment: real VRCPlayerApi usage is
        // Play-Mode-only).
        private static void AssertDoesNotOverride(string methodName)
        {
            MethodInfo match = null;
            foreach (MethodInfo candidate in typeof(TsTimer).GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (candidate.Name == methodName && candidate.GetParameters().Length == 1)
                {
                    match = candidate;
                    break;
                }
            }

            Assert.IsNotNull(match, methodName + "(1 param) not found on TsTimer. Signature changed?");
            Assert.AreEqual(typeof(Tsvrc.Core.TsProcess), match.DeclaringType);
        }

        [Test]
        public void DoesNotOverride_OnPlayerLeft()
        {
            AssertDoesNotOverride("OnPlayerLeft");
        }

        [Test]
        public void DoesNotOverride_OnOwnershipTransferred()
        {
            AssertDoesNotOverride("OnOwnershipTransferred");
        }

        [Test]
        public void DoesNotOverride_OnPlayerSuspendChanged()
        {
            AssertDoesNotOverride("OnPlayerSuspendChanged");
        }
    }
}
