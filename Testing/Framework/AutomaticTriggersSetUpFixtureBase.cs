using System;
using NUnit.Framework;
using Tsvrc.Editor;

namespace Tsvrc.Testing.Framework
{
    /// <summary>
    /// Suppresses TsGenerator's reactive hierarchyChanged/asset-watcher triggers for a whole
    /// test assembly's run, so no test's temp/synthetic scene (which never has a real project's
    /// TsConfig) can cause a real regeneration pass that overwrites the real Assets/TsGenerated.
    ///
    /// NUnit only discovers a <c>[SetUpFixture]</c> for the assembly it's physically compiled
    /// into - a base class living here in a referenced assembly isn't enough by itself. Every
    /// test assembly (Tsvrc's own EditMode/PlayMode suites, and any consuming world's) still
    /// needs its own tiny, namespace-less subclass:
    /// <code>
    /// [SetUpFixture]
    /// public class AutomaticTriggersSetUpFixture : AutomaticTriggersSetUpFixtureBase { }
    /// </code>
    /// That one-line subclass is the only per-assembly boilerplate left; this base class is
    /// where the actual suppression logic lives, exactly once.
    /// </summary>
    public abstract class AutomaticTriggersSetUpFixtureBase
    {
        private IDisposable _scope;

        [OneTimeSetUp]
        public void OneTimeSetUp() => _scope = TsGenerator.SuppressAutomaticTriggers();

        [OneTimeTearDown]
        public void OneTimeTearDown() => _scope.Dispose();
    }
}
