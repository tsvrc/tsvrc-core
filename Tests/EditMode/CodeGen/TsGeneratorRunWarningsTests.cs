using System;
using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.EditMode
{
    // TsGenerator.LastRunWarnings captures Warning and Error log messages prefixed with
    // "[ModuleName]", "[Tsvrc]", or "[TsGenerator]" emitted during the most recent Run() pass,
    // so TsWindow can point at them instead of a user only finding out by happening to have the
    // Console open.
    public class TsGeneratorRunWarningsTests
    {
        private TsGeneratorTestHarness _harness;
        private TempSceneScope _scope;

        [SetUp]
        public void SetUp()
        {
            _harness = new TsGeneratorTestHarness();
            _scope = _harness.Scope;
        }

        [TearDown]
        public void TearDown() => _harness.Dispose();

        [Test]
        public void Run_ConstructsHasNullEntry_LastRunWarningsCapturesTheLoggedWarning()
        {
            // ConstructModule.Resolve() logs this unconditionally during LoadConfig(), which
            // runs on every pass regardless of which branch Run() ultimately takes, giving a
            // reliable, minimal way to exercise the capture mechanism itself.
            var configGo = _scope.CreateGameObject("TsConfig");
            var config = configGo.AddComponent<TsConfig>();
            config.ConstructEntries = new[] { new TsGroupedEntry { Value = null, GroupId = 0 } };

            LogAssert.Expect(UnityEngine.LogType.Warning,
                "[ConstructModule] Null entry in Constructs config, remove the missing-script slot.");

            TsGenerator.Run(skipRefresh: true, allowBootstrap: true);

            CollectionAssert.Contains(TsGenerator.LastRunWarnings,
                "[ConstructModule] Null entry in Constructs config, remove the missing-script slot.");
        }

        [Test]
        public void Run_NoWarningsLogged_LastRunWarningsIsEmpty()
        {
            var configGo = _scope.CreateGameObject("TsConfig");
            configGo.AddComponent<TsConfig>();

            TsGenerator.Run(skipRefresh: true, allowBootstrap: true);

            Assert.IsEmpty(TsGenerator.LastRunWarnings);
        }

        [Test]
        public void Run_PreviousWarningResolved_LastRunWarningsClearsOnNextCleanPass()
        {
            var configGo = _scope.CreateGameObject("TsConfig");
            var config = configGo.AddComponent<TsConfig>();
            config.ConstructEntries = new[] { new TsGroupedEntry { Value = null, GroupId = 0 } };

            LogAssert.Expect(UnityEngine.LogType.Warning,
                "[ConstructModule] Null entry in Constructs config, remove the missing-script slot.");
            TsGenerator.Run(skipRefresh: true, allowBootstrap: true);
            Assert.IsNotEmpty(TsGenerator.LastRunWarnings);

            config.ConstructEntries = Array.Empty<TsGroupedEntry>();
            TsGenerator.Run(skipRefresh: true, allowBootstrap: true);

            Assert.IsEmpty(TsGenerator.LastRunWarnings);
        }
    }
}
