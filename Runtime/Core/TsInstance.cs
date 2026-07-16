using VRC.SDKBase;

namespace Tsvrc.Core
{
    public class TsInstance : TsBehaviour
    {
        // TODO: implement custom master logic (override who is considered master per-instance)
        /// <summary>
        /// Returns <c>true</c> if the local player is the master of this instance.
        /// Defaults to the VRChat API master (<see cref="Networking.IsMaster"/>).
        /// </summary>
        public virtual bool IsTsMaster => Networking.IsMaster;

        /// <summary>
        /// Called when this instance is started. Override this method to perform any initialization logic that requires the instance to be fully constructed.
        /// </summary>
        public virtual void OnInstanceStart() { }
    }
}
