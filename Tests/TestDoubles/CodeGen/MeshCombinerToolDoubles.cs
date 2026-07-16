using UdonSharp;

namespace Tsvrc.Tests.Editor
{
    // Plain marker UdonSharpBehaviour; MeshCombinerTool only checks for its presence via
    // GetComponent<UdonSharpBehaviour>() to decide whether to skip collider collection.
    public class MeshCombinerToolUdonTestBehaviour : UdonSharpBehaviour
    {
    }
}
