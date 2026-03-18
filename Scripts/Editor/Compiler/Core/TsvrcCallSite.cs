#if UNITY_EDITOR
namespace Tsvrc.Editor
{
    internal class TsvrcCallSite
    {
        internal string ClassName; // class that contains the call (file stem)
        internal string FileName;  // full path (for diagnostics)
    }
}
#endif
