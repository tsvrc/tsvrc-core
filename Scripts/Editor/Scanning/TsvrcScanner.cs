#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace Tsvrc.Editor
{
    internal static class TsvrcScanner
    {
        internal static TsvrcScanResult Scan()
        {
            var config = TsvrcConfigReader.Read();
            if (config == null) return null;

            var instanceType = TsvrcConfigReader.DetectInstanceType();

            var result = new TsvrcScanResult
            {
                SourceConfig = config,
                InstanceType = instanceType
            };

            var usedNames = new HashSet<string>();

            var singletonGroup = TsvrcSingletonScanner.Scan(config, usedNames);
            if (singletonGroup == null) return null;

            var factoryGroup = TsvrcBehaviourScanner.ScanFactory(config, usedNames);
            var constructGroup = TsvrcBehaviourScanner.ScanConstruct(config, usedNames);

            if (singletonGroup.Entries.Count == 0 && factoryGroup.Entries.Count == 0 && constructGroup.Entries.Count == 0)
            {
                Debug.LogWarning("[TsvrcCompiler] All arrays are empty. Nothing to generate.");
                return null;
            }

            if (singletonGroup.Entries.Count > 0) result.Groups.Add(singletonGroup);
            if (factoryGroup.Entries.Count > 0) result.Groups.Add(factoryGroup);
            if (constructGroup.Entries.Count > 0) result.Groups.Add(constructGroup);

            return result;
        }
    }
}
#endif
