#if UNITY_EDITOR
using Tsvrc.Core;

namespace Tsvrc.Editor
{
    internal static class SingletonScanner
    {
        internal static ScanResult Scan(TsvrcConfig config)
        {
            ScanResult result = new ScanResult();

            var singletons = config.Singletons;
            var internalSingletons = config.InternalTsvrcConfig.Singletons;

            foreach (var singleton in singletons)
            {
                if (singleton == null) continue;

                result.Namespaces.Add(singleton.GetType().Namespace);
            }

            foreach (var singleton in internalSingletons)
            {
                if (singleton == null) continue;

                result.Namespaces.Add(singleton.GetType().Namespace);
            }

            return result;
        }
    }
}
#endif