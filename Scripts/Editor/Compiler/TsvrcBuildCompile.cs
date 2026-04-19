#if UNITY_EDITOR
using VRC.SDKBase.Editor.BuildPipeline;

namespace Tsvrc.Editor
{
    /// <summary>
    /// Ensures Tsvrc is compiled before every VRChat world build so the uploaded content is
    /// always in sync with the current config and source files.
    ///
    /// Runs early (order -100) so the generated CompiledTsvrc.cs and scene wiring are settled
    /// before UdonSharp's own build-time compilation pass.
    /// </summary>
    internal class TsvrcBuildCompile : IVRCSDKBuildRequestedCallback
    {
        public int callbackOrder => -100;

        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            // Skip AssetDatabase.Refresh — calling it mid-build pipeline can trigger a domain
            // reload and corrupt the build or cause UdonSharp to recompile at the wrong time.
            TsvrcCompiler.Compile(refreshAssetDatabase: false);
            return true;
        }
    }
}
#endif
