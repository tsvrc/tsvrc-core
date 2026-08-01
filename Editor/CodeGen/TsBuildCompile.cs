#if UNITY_EDITOR
using UnityEditor;
using VRC.SDKBase.Editor.BuildPipeline;

namespace Tsvrc.Editor
{
    // Ensures generated files and scene wiring are up to date before every VRChat world build.
    // Runs at order -99 so it settles before UdonSharp's build pass (order 0 by default).
    // skipRefresh: calling AssetDatabase.Refresh() mid-build can corrupt the upload or trigger
    // an unexpected domain reload — we write files but skip the refresh here.
    internal class TsBuildCompile : IVRCSDKBuildRequestedCallback
    {
        public int callbackOrder => -99;

        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            if (requestedBuildType != VRCSDKRequestedBuildType.Scene)
                return true;

            // Checked before HasBootstrapSignal() below, since that check's type-based fallback
            // (any Instance subclass compiled anywhere) can't distinguish "the linked scene is
            // open and correct" from "some other scene entirely is open". IsConfiguredButNotLoaded
            // is true whenever the scene actually being built isn't the one Tsvrc is scoped to,
            // whether a different scene is open or the linked one was deleted.
            string wrongSceneWarning = DetermineWrongSceneBuildWarning(
                TsLinkedScene.IsConfiguredButNotLoaded, TsLinkedScene.ScenePath);
            if (wrongSceneWarning != null)
            {
                bool cancelWrongScene = EditorUtility.DisplayDialog(
                    "Tsvrc Linked Scene Not Open", wrongSceneWarning, "Cancel Build", "Build Anyway");
                if (cancelWrongScene) return false;
            }

            TsGenerator.AfterDomainReload(skipRefresh: true);

            // HasBootstrapSignal() can still be false here, since the pass above defaults to allowBootstrap: false.
            if (TsGenerator.HasBootstrapSignal()) return true;

            bool cancelBuild = EditorUtility.DisplayDialog(
                "Tsvrc Not Initialized",
                "Tsvrc has never been set up in this scene: no TsConfig, no generated TsGenerated " +
                "object, and no Instance subclass found anywhere in the project. Every TsvrcBehaviour " +
                "script's root reference will be null at runtime.\n\n" +
                "Open Tsvrc > Configure and click \"Initialize Tsvrc\" before building, or continue " +
                "anyway if this is intentional.",
                "Cancel Build", "Build Anyway");
            return !cancelBuild;
        }

        // Pure so it's directly unit-testable without driving a real VRChat SDK build or
        // EditorUtility.DisplayDialog. Returns null when there's nothing to warn about (nothing
        // linked yet, or the linked scene is genuinely open).
        internal static string DetermineWrongSceneBuildWarning(bool isConfiguredButNotLoaded, string linkedScenePath)
        {
            if (!isConfiguredButNotLoaded) return null;
            return $"Tsvrc is linked to '{linkedScenePath}', but that scene is not currently open (or it no " +
                "longer exists). Building now would ship whatever generated/wired state already happens to be " +
                "on disk, which may be stale or simply wrong for the scene actually being built.\n\n" +
                "Open the linked scene before building, or continue anyway if this is intentional.";
        }
    }
}
#endif
