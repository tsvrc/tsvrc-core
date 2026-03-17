using System.Collections.Generic;

namespace Tsvrc.Editor
{
    internal class TsvrcField
    {
        internal string Type;
        internal string Name;
        internal string Namespace;
        internal List<TsvrcCallSite> CallSites = new List<TsvrcCallSite>();
    }
}