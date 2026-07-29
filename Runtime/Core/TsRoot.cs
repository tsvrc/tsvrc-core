using Tsvrc.Utils;
using UdonSharp;

namespace Tsvrc.Core.Generated
{
    // The root object every TsvrcBehaviour reaches via _ts. TsGenerated (the per-project
    // generated class) extends this. TsvrcBehaviour depends only on TsRoot, never on
    // TsGenerated, so Tsvrc.Runtime compiles standalone before the generator has run and
    // never references generated code.
    public abstract class TsRoot : UdonSharpBehaviour
    {
        public virtual Instance Instance => null;
        public virtual TsMemory Memory => null;
        public virtual TsLogger Log => null;
    }
}
