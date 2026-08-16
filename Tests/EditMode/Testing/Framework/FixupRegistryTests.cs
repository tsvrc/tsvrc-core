using System.Linq;
using NUnit.Framework;
using Tsvrc.Testing.Framework;

namespace Tsvrc.Tests.EditMode.Testing.Framework
{
    // FixupRegistry backs every fixup TsPlayModeTestBase applies automatically. Its state
    // (DisabledTypes) is a static, process-wide set, so every test here restores it via
    // EnableAll() in [TearDown] - leaving a fixup disabled would silently break every other
    // PlayMode test in the run that depends on it (e.g. ClientSimPersistenceLeakFixup's leak
    // cleanup between ClientSim sessions).
    public class FixupRegistryTests
    {
        [TearDown]
        public void TearDown() => FixupRegistry.EnableAll();

        [Test]
        public void ActiveFixups_Default_IncludesClientSimPersistenceLeakFixup()
        {
            Assert.IsTrue(FixupRegistry.ActiveFixups.Any(f => f is ClientSimPersistenceLeakFixup));
        }

        [Test]
        public void Disable_KnownFixupType_RemovesItFromActiveFixups()
        {
            FixupRegistry.Disable<ClientSimPersistenceLeakFixup>();

            Assert.IsFalse(FixupRegistry.ActiveFixups.Any(f => f is ClientSimPersistenceLeakFixup));
        }

        [Test]
        public void Disable_CalledTwiceForSameType_IsIdempotent()
        {
            FixupRegistry.Disable<ClientSimPersistenceLeakFixup>();

            Assert.DoesNotThrow(() => FixupRegistry.Disable<ClientSimPersistenceLeakFixup>());
            Assert.IsFalse(FixupRegistry.ActiveFixups.Any(f => f is ClientSimPersistenceLeakFixup));
        }

        [Test]
        public void EnableAll_AfterDisable_RestoresTheFixup()
        {
            FixupRegistry.Disable<ClientSimPersistenceLeakFixup>();

            FixupRegistry.EnableAll();

            Assert.IsTrue(FixupRegistry.ActiveFixups.Any(f => f is ClientSimPersistenceLeakFixup));
        }

        [Test]
        public void EnableAll_WithNothingDisabled_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => FixupRegistry.EnableAll());
        }

        [Test]
        public void ActiveFixups_UnrelatedTypeNeverRegistered_DisablingItDoesNotAffectRealFixups()
        {
            // A type that implements the interface but was never added to FixupRegistry's own
            // catalogue - disabling it must be a harmless no-op, not throw or affect anyone else.
            FixupRegistry.Disable<UnregisteredFixupDouble>();

            Assert.IsTrue(FixupRegistry.ActiveFixups.Any(f => f is ClientSimPersistenceLeakFixup));
        }

        private sealed class UnregisteredFixupDouble : IPlayModeEnvironmentFixup
        {
            public void OnBeforeAnyTests()
            {
            }

            public void OnUnityTearDown()
            {
            }
        }
    }
}
