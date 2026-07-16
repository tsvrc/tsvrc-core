using NUnit.Framework;
using Tsvrc.Editor;
using UdonSharpEditor;
using UnityEngine;

namespace Tsvrc.Tests.Editor
{
    // Adds a real, compiled TsGenerated instance (there is only ever one such type in
    // the AppDomain) to a fresh GameObject inside a TempSceneScope. Module Wire()/
    // GenerateCode() tests then call through real production code against this synthetic
    // root, never the developer's actual scaffolded scene object.
    internal static class CompiledRootFixture
    {
        // Mirrors ScaffoldModule's own (private) GeneratedFolder/ScaffoldFilePath/
        // GeneratedAssetPath constants - always project-Assets-rooted, never package-relative.
        private const string ScriptPath = "Assets/TsGenerated/" + ScaffoldModule.CompiledClassName + ".cs";
        private const string AssetPath = "Assets/TsGenerated/" + ScaffoldModule.CompiledClassName + ".asset";

        internal static Component AddTo(TempSceneScope scope, string name = "TestRoot_TsGenerated")
        {
            var compiledType = ScaffoldModule.FindCompiledType();
            if (compiledType == null)
            {
                Assert.Ignore("Compiled TsGenerated type not found in the AppDomain - " +
                    "run Tsvrc > Force Regenerate once (or wait for a domain reload) before " +
                    "running CodeGen wiring tests.");
                return null;
            }

            // UdonSharpUndo.AddComponent needs a real UdonSharpProgramAsset linking the
            // compiled script before it will validate/add the behaviour. In a project that
            // hasn't been bootstrapped through the Editor yet (generated .cs files present,
            // but ScaffoldModule.AfterFilesStable() never ran to create the .asset), this is
            // missing - so self-heal it here exactly the way production code does, rather
            // than requiring a manual step.
            if (!ScaffoldModule.EnsureUdonSharpProgramAsset(ScriptPath, AssetPath))
            {
                Assert.Ignore($"Could not create/find the UdonSharpProgramAsset for {ScaffoldModule.CompiledClassName} " +
                    $"at '{AssetPath}' (script at '{ScriptPath}'). Open the project in the Unity Editor once and run " +
                    "Tsvrc > Force Regenerate to bootstrap it.");
                return null;
            }

            var go = scope.CreateGameObject(name);
            var component = UdonSharpUndo.AddComponent(go, compiledType) as Component;
            Assert.IsNotNull(component, "Failed to add compiled TsGenerated component to the test fixture GameObject.");
            return component;
        }
    }
}
