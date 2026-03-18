#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using Tsvrc.Core;

namespace Tsvrc.Editor
{
    internal class PoolScanner : Scanner
    {
        internal override ScanResult Scan(TsvrcConfig config)
        {
            ScanResult result = new ScanResult();

            var pools = config.TsvrcBehaviourPool;
            var internalPools = config.InternalTsvrcConfig.TsvrcBehaviourPool;

            var usedNames = new HashSet<string>();
            var objects = pools.Union(internalPools).Cast<UnityEngine.Object>().ToHashSet();

            var fields = TsvrcResolver.Resolve(objects, usedNames);

            result.Fields = fields;

            var builder = new PoolBuilder();
            result.Builder = builder;
            return result;
        }
    }
}
#endif