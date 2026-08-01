using Tsvrc.Core;

namespace Tsvrc.Tests.EditMode
{
    // Dedicated single-class file - same reasoning as InstanceModuleWireTestDouble's own doc
    // comment: InstanceModuleWireTests locates a stand-in type's script by filename, which only
    // works when the class name matches its containing file's name exactly.
    //
    // Unlike InstanceModuleWireTestDouble (deliberately NOT an Instance subclass, so it's never
    // mistaken for the project's real scaffold instance by TsGenerator.HasBootstrapSignal()/
    // InstanceModule.DetectInstanceType()), this one specifically does need to be a real Instance
    // subclass: a stale Instance subclass left behind by a retype/rename must still be destroyed,
    // unlike a genuinely unrelated UdonSharpBehaviour sharing the same child GameObject.
    // [TsCodegenIgnore] keeps it excluded from both of those live scans regardless, the same way
    // InstanceTestSubclass already is.
    [TsCodegenIgnore]
    public class InstanceModuleWireStaleTestDouble : Instance
    {
    }
}
