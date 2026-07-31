using System;
using Tsvrc.Editor;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // Bundles everything a test driving the real, static TsGenerator.Run() or
    // AfterDomainReload() needs to do so safely: a temp scene, never the developer's real one,
    // TsPaths.GeneratedFolder redirected to a scratch folder so WriteModules()'s real
    // File.WriteAllText calls never touch the consuming project's actual Assets/TsGenerated,
    // and the PendingBootstrap SessionState flag plus RunCore's hierarchyChanged and
    // postprocessModifications hooks reset before and after. The generated folder redirect used
    // to be handled by backing up and restoring the real files after the fact, which left a
    // corruption window if the test process was interrupted before TearDown ran.
    //
    // Note that CompiledClassName is not redirected here. A test that also needs an already
    // compiled root present, see CompiledRootFixture.AddTo, still calls that explicitly, since
    // not every Run()-driving test needs one. Some are specifically about the "written to
    // scratch, not yet compiled" pathway, see TsGeneratorBootstrapPersistenceTests.
    internal sealed class TsGeneratorTestHarness : IDisposable
    {
        // Duplicated from TsGenerator's own private PendingBootstrapKey const, since it isn't
        // exposed, and every pre-existing test in this suite that touches SessionState directly
        // already duplicates this same literal rather than reflecting it out.
        private const string PendingBootstrapKey = "Tsvrc.PendingBootstrap";

        internal TempSceneScope Scope { get; }

        internal TsGeneratorTestHarness()
        {
            SessionState.EraseBool(PendingBootstrapKey);
            Scope = new TempSceneScope();
            ScratchAssets.EnsureFolder();
            TsPaths.GeneratedFolder = ScratchAssets.Folder + "/GeneratedScratch";
        }

        internal GameObject CreateGameObject(string name) => Scope.CreateGameObject(name);

        public void Dispose()
        {
            // Resets the hierarchyChanged and Undo.postprocessModifications subscriptions
            // RunCore installs on a successful pass, along with _activeModules and
            // _watchedComponentTypeNames. Otherwise a later, unrelated test's scene edits could
            // spuriously schedule a rerun left over from this test. Must run before Scope.Dispose()
            // resets TsPaths back to real values: HasBootstrapSignal() reacts to a loaded Instance
            // subclass anywhere in the AppDomain, not scene content, so in a consuming project
            // that has one (a real world's Instance subclass, always loaded regardless of scene)
            // this never short-circuits, and calling it after the redirect is gone would run a
            // real pass against the actual project's Assets/TsGenerated.
            TsGenerator.AfterDomainReload(skipRefresh: true);
            Scope.Dispose(); // also resets TsPaths (GeneratedFolder, CompiledClassName, ...) to defaults
            ScratchAssets.DeleteAll();
            SessionState.EraseBool(PendingBootstrapKey);
        }
    }
}
