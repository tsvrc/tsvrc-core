#if UNITY_EDITOR
using System.Collections.Generic;
using Tsvrc.Core;

namespace Tsvrc.Editor
{
    internal static class TsvrcScanner
    {
        internal static List<ScanResult> Scan(TsvrcConfig config)
        {
            var singletonResult = new SingletonScanner().Scan(config);
            var poolResult = new PoolScanner().Scan(config);

            var results = new List<ScanResult>
            {
                singletonResult,
                poolResult
            };

            return results;
        }
    }
}
#endif
