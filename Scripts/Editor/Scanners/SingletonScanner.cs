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
                var t = singleton.GetType();
                result.Namespaces.Add(t.Namespace);
                result.Types.Add(t.Name);
            }

            foreach (var singleton in internalSingletons)
            {
                if (singleton == null) continue;
                var t = singleton.GetType();
                result.Namespaces.Add(t.Namespace);
                result.Types.Add(t.Name);
            }

            var builder = new SingletonBuilder();
            result.Builder = builder;
            return result;
        }
    }
}
#endif