using NUnit.Framework;
using Tsvrc.Config;
using Tsvrc.Editor;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.EditMode
{
    // TsGenerator.LastRunWarnings - captures [ModuleName]/[Tsvrc]/[TsGenerator]-prefixed
    // Warning/Error log messages emitted during the most recent Run() pass, so TsWindow can point
    // at them instead of a user only finding out by happening to have the Console open.
    public class TsGeneratorRunWarningsTests
    {
        private TempSceneScope _scope;
        private GeneratedFileBackup _backup;

        [SetUp]
        public void SetUp()
        {
            _backup = new GeneratedFileBackup();
            _scope = new TempSceneScope();
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
            _backup.Dispose();
            TsGenerator.AfterDomainReload(skipRefresh: true); // reset hooks, see TsBuildCompileTests
        }

        [Test]
        public void Run_ConstructsHasNullEntry_LastRunWarningsCapturesTheLoggedWarning()
        {
            // ConstructModule.Resolve() logs this unconditionally during LoadConfig(), which runs
            // on every pass regardless of which branch Run() ultimately takes - a reliable,
            // minimal way to exercise the capture mechanism itself.
            var configGo = _scope.CreateGameObject("TsConfig");
            var config = configGo.AddComponent<TsConfig>();
            config.Constructs = new Tsvrc.Core.TsBehaviour[] { null };

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
            config.Constructs = new Tsvrc.Core.TsBehaviour[] { null };

            LogAssert.Expect(UnityEngine.LogType.Warning,
                "[ConstructModule] Null entry in Constructs config, remove the missing-script slot.");
            TsGenerator.Run(skipRefresh: true, allowBootstrap: true);
            Assert.IsNotEmpty(TsGenerator.LastRunWarnings);

            config.Constructs = new Tsvrc.Core.TsBehaviour[0];
            TsGenerator.Run(skipRefresh: true, allowBootstrap: true);

            Assert.IsEmpty(TsGenerator.LastRunWarnings);
        }
    }
}
