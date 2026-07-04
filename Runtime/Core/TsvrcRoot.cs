using Tsvrc.Utils;
using UdonSharp;

namespace Tsvrc.Core.Generated
{
    // The root object every TsvrcBehaviour reaches via _ts. TsvrcGenerated (the per-project
    // generated class) extends this. TsvrcBehaviour depends only on TsvrcRoot, never on
    // TsvrcGenerated, so Tsvrc.Runtime compiles standalone before the generator has run and
    // never references generated code.
    public abstract class TsvrcRoot : UdonSharpBehaviour
    {
        public virtual TsvrcInstance Instance => null;
        public virtual TsMemory Memory => null;
    }
}
