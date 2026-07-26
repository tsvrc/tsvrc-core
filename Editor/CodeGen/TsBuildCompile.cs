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

            TsGenerator.AfterDomainReload(skipRefresh: true);

            // HasBootstrapSignal() can still be false here, since the pass above defaults to allowBootstrap: false.
            if (TsGenerator.HasBootstrapSignal()) return true;

            bool cancelBuild = EditorUtility.DisplayDialog(
                "Tsvrc Not Initialized",
                "Tsvrc has never been set up in this scene: no TsConfig, no generated TsGenerated " +
                "object, and no TsInstance subclass found anywhere in the project. Every TsBehaviour " +
                "script's root reference will be null at runtime.\n\n" +
                "Open Tsvrc > Configure and click \"Initialize Tsvrc\" before building, or continue " +
                "anyway if this is intentional.",
                "Cancel Build", "Build Anyway");
            return !cancelBuild;
        }
    }
}
#endif
