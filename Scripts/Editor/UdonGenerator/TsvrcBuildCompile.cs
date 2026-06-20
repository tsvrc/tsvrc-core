#if UNITY_EDITOR
using VRC.SDKBase.Editor.BuildPipeline;

namespace Tsvrc.Editor
{
    // Ensures generated files and scene wiring are up to date before every VRChat world build.
    // Runs at order -99 so it settles before UdonSharp's build pass (order 0 by default).
    // skipRefresh: calling AssetDatabase.Refresh() mid-build can corrupt the upload or trigger
    // an unexpected domain reload — we write files but skip the refresh here.
    internal class TsvrcBuildCompile : IVRCSDKBuildRequestedCallback
    {
        public int callbackOrder => -99;

        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            if (requestedBuildType != VRCSDKRequestedBuildType.Scene)
                return true;

            TsvrcGenerator.AfterDomainReload(skipRefresh: true);
            return true;
        }
    }
}
#endif
