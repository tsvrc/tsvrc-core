using Tsvrc.Core.Generated;
using Tsvrc.Editor;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // Adds the permanent TestGenerated double (Tests/TestDoubles/CodeGen/TestGenerated.cs) to
    // a fresh GameObject inside a TempSceneScope, and redirects TsPaths.CompiledClassName so
    // ScaffoldModule.FindCompiledType() and FindRoot() resolve to it. Module Wire() tests then
    // call through real production code against this synthetic root, never a consuming
    // project's real scaffolded scene object, and never conditional on whether that project has
    // been generated yet, since TestGenerated is compiled unconditionally as part of this test
    // assembly.
    //
    // Uses a plain AddComponent rather than UdonSharpUndo.AddComponent, since that call exists
    // to also set up the hidden backing UdonBehaviour a real UdonSharpProgramAsset provides,
    // which none of the Wire()-level, SerializedObject-based tests this fixture serves actually
    // need. They only read and write C# level fields. Tests that exercise ScaffoldModule's own
    // AddComponent and program asset creation code directly, see ScaffoldModuleWireTests,
    // redirect TsPaths.ScaffoldScriptPath at TestGenerated.cs's real location instead, so that
    // production code path gets a real MonoScript to work with.
    internal static class CompiledRootFixture
    {
        internal static Component AddTo(TempSceneScope scope, string name = "TestRoot_TestGenerated")
        {
            TsPaths.CompiledClassName = nameof(TestGenerated);
            var go = scope.CreateGameObject(name);
            return go.AddComponent<TestGenerated>();
        }
    }
}
