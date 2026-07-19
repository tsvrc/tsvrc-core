using Tsvrc.Core;

namespace Tsvrc.Tests.EditMode
{
    // Dedicated single-class file - unlike TsBehaviourTestSubclass (which lives in
    // TsBehaviourDoubles.cs alongside several other classes), this file's name matches its
    // one class exactly, so it has its own findable MonoScript asset. InstanceModuleWireTests
    // needs that: it locates a stand-in type's script by filename (mirroring
    // InstanceModule.FindScriptAssetPath's own lookup), which only works for a type whose
    // class name equals its containing file's name. A real, compiled UdonSharpBehaviour with
    // no production UdonSharpProgramAsset anywhere else in the project.
    public class InstanceModuleWireTestDouble : TsBehaviour
    {
    }
}
