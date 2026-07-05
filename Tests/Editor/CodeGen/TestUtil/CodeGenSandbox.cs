using System.IO;
using Tsvrc.Config;
using Tsvrc.Core;
using Tsvrc.Editor;
using Tsvrc.StateMachine;
using UdonSharp;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Tsvrc.Tests.Editor
{
    // Permanent sandbox tooling for exercising the CodeGen happy paths that need real,
    // config-derived fields on the compiled TsvrcGenerated type (Singleton/Construct/
    // Factory/Pool - see CODEGEN_TESTING_PLAN.md Part 4.5). Entirely C#/Unity-native: no
    // external shell/PowerShell scripts, just three ordinary `Unity.exe -batchmode`
    // invocations (the middle one is the same plain `-runTests` CLI already used
    // throughout this project's test suite - proven reliable all session):
    //
    //   1. Unity.exe -batchmode -projectPath <repo> -quit
    //        -executeMethod Tsvrc.Tests.Editor.CodeGenSandbox.Bootstrap
    //   2. Unity.exe -batchmode -projectPath <repo>
    //        -runTests -testPlatform EditMode -testResults results.xml
    //        (SandboxGate-gated happy-path tests now execute for real instead of
    //        Assert.Ignore()'ing)
    //   3. Unity.exe -batchmode -projectPath <repo> -quit
    //        -executeMethod Tsvrc.Tests.Editor.CodeGenSandbox.Restore
    //
    // Why three separate process launches rather than one continuous run: writing new
    // fields to Assets/TsvrcGenerated/*.cs only takes effect once Unity recompiles from
    // that new source, which means a fresh process. An earlier version of this tool tried
    // to do steps 1-2 in a single long-lived batch process (triggering the recompile and
    // waiting it out via SessionState + [InitializeOnLoad], then driving TestRunnerApi
    // programmatically) - that hit the same Test-Runner-never-terminates fragility already
    // documented in TESTING_PLAN.md's "Known environment constraint" section. Three
    // separate, ordinary invocations sidestep that fragility entirely.
    //
    // Bootstrap()/Restore() never edit Assets/Scenes/VRCDefaultWorldScene.unity (or any
    // committed scene) - the config lives on a throwaway GameObject in a brand-new scene
    // that Bootstrap() itself discards (replaces with another fresh, unmodified empty
    // scene) before returning, so nothing is left "dirty" going into step 2. The only real,
    // persistent side effects between steps 1 and 3 are (a) the rewritten
    // Assets/TsvrcGenerated/*.cs content (backed up to the OS temp folder by Bootstrap(),
    // restored by Restore()), (b) two scratch prefab assets plus a scratch language JSON
    // file under ScratchFolder, and (c) TranslationModule's own fixed-path config asset
    // (Assets/TsvrcGenerated/TsvrcTranslationConfig.asset, which doesn't exist in this
    // project otherwise) - (b) and (c) are both deleted by Restore(). Nothing is left in
    // the working tree once step 3 finishes.
    internal static class CodeGenSandbox
    {
        internal const string ScratchFolder = "Assets/Tsvrc/__TestBootstrapScratch__";
        internal const string FactoryPrefabPath = ScratchFolder + "/SampleFactoryPrefab.prefab";
        internal const string PoolPrefabPath = ScratchFolder + "/SamplePoolPrefab.prefab";
        internal const string LanguageFilePath = ScratchFolder + "/SampleLanguage.json";

        // Matches TranslationModule.ConfigAssetPath (private) - a fixed path the module
        // itself owns entirely; this project has no real translation config yet (confirmed:
        // no such asset exists in a fresh clone), so the sandbox exclusively creates and
        // later deletes it rather than needing to back it up/restore like the shared
        // Assets/TsvrcGenerated/*.cs files.
        internal const string TranslationConfigAssetPath = "Assets/TsvrcGenerated/TsvrcTranslationConfig.asset";

        private const string GeneratedFolder = "Assets/TsvrcGenerated";
        private static readonly string[] GeneratedFileNames =
        {
            "TsvrcGenerated.cs", "TsvrcGeneratedConstruct.cs", "TsvrcGeneratedFactory.cs",
            "TsvrcGeneratedInstance.cs", "TsvrcGeneratedMemory.cs", "TsvrcGeneratedPool.cs",
            "TsvrcGeneratedSingleton.cs", "TsvrcGeneratedTranslation.cs",
        };

        // Written outside Assets/ (a plain OS temp folder, not an imported asset) so backing
        // up/restoring never itself triggers an import or shows up in the working tree.
        private static string BackupFolder =>
            Path.Combine(Path.GetTempPath(), "tsvrc_codegen_sandbox_backup");

        // Field/type names this bootstrap deterministically produces - permanent test code
        // reads these constants rather than hardcoding magic strings, so the sandbox and its
        // consuming tests can never drift apart silently. "EntryName" is the raw config-alias
        // name (what a module's Resolve() step derives from); "FieldName" is the final
        // generated field name after each module's own prefix convention is applied.
        internal const string SingletonFieldName = "SampleSingleton";
        internal const string ConstructEntryName = "SampleConstruct";
        internal const string ConstructFieldName = "_constructSampleConstruct";
        internal const string FactoryEntryName = "SampleFactoryPrefab";
        internal const string FactoryFieldName = "_factorySampleFactoryPrefab";
        internal const string FactoryMethodName = "CreateSampleFactoryPrefab";
        internal const string PoolTypeName = "StateManager";
        internal const string TranslationKey = "_sampleTranslationKey_";
        internal const string TranslationEnumMemberName = "English";

        [MenuItem("Tsvrc/CodeGen Sandbox/1) Bootstrap (writes real fields to TsvrcGenerated)")]
        public static void Bootstrap()
        {
            BackupGeneratedFiles();

            // A brand-new scene for the scratch config to live in - never the scene the user
            // happened to have open, and (via the NewScene call at the end of this method)
            // never left "modified" for Unity Test Framework to try to save.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            if (!AssetDatabase.IsValidFolder(ScratchFolder))
                AssetDatabase.CreateFolder("Assets/Tsvrc", "__TestBootstrapScratch__");

            var factoryGo = new GameObject("SampleFactoryPrefab");
            var factoryPrefab = PrefabUtility.SaveAsPrefabAsset(factoryGo, FactoryPrefabPath);
            Object.DestroyImmediate(factoryGo);

            var poolGo = new GameObject("SamplePoolPrefab");
            poolGo.AddComponent<StateManager>();
            var poolPrefab = PrefabUtility.SaveAsPrefabAsset(poolGo, PoolPrefabPath);
            Object.DestroyImmediate(poolGo);

            // A [WirePool] StateManager consumer so PoolModule.ScanExternalRefs() computes a
            // non-zero slot count for StateManager (TotalSlots==0 would otherwise exclude it
            // from generation entirely).
            new GameObject("SamplePoolConsumer").AddComponent<PoolWireTargetDouble>();

            // TranslationModule reads from its own fixed asset path, not from TsvrcConfig -
            // write the language file directly and create the config asset there.
            string languageJson = "{\"key\":\"en\",\"label\":\"" + TranslationEnumMemberName + "\"," +
                "\"entries\":{\"" + TranslationKey + "\":\"Hello\"}}";
            File.WriteAllText(ToFullPath(LanguageFilePath), languageJson);
            AssetDatabase.ImportAsset(LanguageFilePath);
            var languageAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(LanguageFilePath);

            var translationConfig = ScriptableObject.CreateInstance<TsvrcTranslationConfig>();
            translationConfig.LanguageFiles = new[] { languageAsset };
            AssetDatabase.CreateAsset(translationConfig, TranslationConfigAssetPath);

            var configGo = new GameObject("__TestBootstrapConfig__");
            var config = configGo.AddComponent<TsvrcConfig>();

            var singletonGo = new GameObject($"__{SingletonFieldName}__");
            config.Singletons = new Object[] { singletonGo };

            var constructGo = new GameObject("__SampleConstruct__");
            var constructBehaviour = constructGo.AddComponent<StateManager>();
            config.Constructs = new TsvrcBehaviour[] { constructBehaviour };

            config.Factories = new[]
            {
                new TsvrcFactoryGroup { GroupName = "", Prefabs = new Object[] { factoryPrefab } },
            };
            config.PooledObjects = new UdonSharpBehaviour[] { poolPrefab.GetComponent<StateManager>() };

            TsvrcGenerator.Run(skipRefresh: true, allowBootstrap: true);

            // Discard the scratch scene now that its only job (feeding LoadConfig()/
            // GenerateCode() once) is done - replacing it with a fresh, unmodified empty
            // scene means nothing is left dirty for the test runner to prompt about.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Debug.Log("[CodeGenSandbox] Bootstrap complete - Assets/TsvrcGenerated/*.cs now contain real sample fields. " +
                "Run the EditMode suite now, then call Restore() to put everything back.");
        }

        [MenuItem("Tsvrc/CodeGen Sandbox/2) Restore (reverts TsvrcGenerated, deletes scratch assets)")]
        public static void Restore()
        {
            RestoreGeneratedFiles();

            if (AssetDatabase.IsValidFolder(ScratchFolder))
                AssetDatabase.DeleteAsset(ScratchFolder);

            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(TranslationConfigAssetPath) != null)
                AssetDatabase.DeleteAsset(TranslationConfigAssetPath);

            AssetDatabase.Refresh();
            Debug.Log("[CodeGenSandbox] Restore complete - Assets/TsvrcGenerated and scratch assets are back to their original state.");
        }

        private static string ToFullPath(string assetPath)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void BackupGeneratedFiles()
        {
            Directory.CreateDirectory(BackupFolder);
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            foreach (var fileName in GeneratedFileNames)
            {
                string source = Path.Combine(projectRoot, GeneratedFolder.Replace('/', Path.DirectorySeparatorChar), fileName);
                if (File.Exists(source))
                    File.Copy(source, Path.Combine(BackupFolder, fileName), overwrite: true);
            }
        }

        private static void RestoreGeneratedFiles()
        {
            if (!Directory.Exists(BackupFolder))
            {
                Debug.LogWarning("[CodeGenSandbox] No backup found - Restore() was called without a preceding Bootstrap() in this environment. Nothing to restore.");
                return;
            }

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            foreach (var fileName in GeneratedFileNames)
            {
                string backup = Path.Combine(BackupFolder, fileName);
                if (!File.Exists(backup)) continue;
                string destination = Path.Combine(projectRoot, GeneratedFolder.Replace('/', Path.DirectorySeparatorChar), fileName);
                File.Copy(backup, destination, overwrite: true);
            }

            Directory.Delete(BackupFolder, recursive: true);
        }
    }
}
