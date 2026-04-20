#if UNITY_EDITOR
using VRC.SDKBase.Editor.BuildPipeline;

namespace Tsvrc.Editor
{
    // Ensures Tsvrc is compiled before every VRChat world build so the uploaded content is
    // always in sync with the current config and source files.
    //
    // Runs at order -100 so the generated CompiledTsvrc.cs and scene wiring are settled
    // before UdonSharp's own build-time compilation pass.
    internal class TsvrcBuildCompile : IVRCSDKBuildRequestedCallback
    {
        public int callbackOrder => -100;

        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            // Tsvrc is world-only. Avatar builds must not be blocked.
            if (requestedBuildType != VRCSDKRequestedBuildType.Scene)
                return true;

            // Skip AssetDatabase.Refresh, calling it mid-build pipeline can trigger a domain
            // reload and corrupt the build or cause UdonSharp to recompile at the wrong time.
            // Return false to abort the build if compilation fails (e.g. no TsvrcConfig in scene).
            return TsvrcCompiler.Compile(refreshAssetDatabase: false);
        }
    }
}
#endif
