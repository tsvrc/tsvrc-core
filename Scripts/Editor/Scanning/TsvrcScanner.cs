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
            var internalConfig = config.InternalTsvrcConfig;

            // ── Core groups (InternalTsvrcConfig) — added first so they appear before user entries ──
            if (internalConfig != null)
            {
                var coreSingletons = TsvrcSingletonScanner.Scan(internalConfig.Singletons, true, usedNames);
                if (coreSingletons == null) return null;
                if (coreSingletons.Entries.Count > 0) result.Groups.Add(coreSingletons);

                var coreFactory = TsvrcBehaviourScanner.ScanFactory(internalConfig.TsvrcBehaviourFactory, true, usedNames);
                if (coreFactory.Entries.Count > 0) result.Groups.Add(coreFactory);
            }

            // ── User groups (TsvrcConfig) ──
            var singletonGroup = TsvrcSingletonScanner.Scan(config.Singletons, false, usedNames);
            if (singletonGroup == null) return null;

            var factoryGroup = TsvrcBehaviourScanner.ScanFactory(config.TsvrcBehaviourFactory, false, usedNames);
            var constructGroup = TsvrcBehaviourScanner.ScanConstruct(config.TsvrcBehaviourConstruct, false, usedNames);

            if (singletonGroup.Entries.Count > 0) result.Groups.Add(singletonGroup);
            if (factoryGroup.Entries.Count > 0) result.Groups.Add(factoryGroup);
            if (constructGroup.Entries.Count > 0) result.Groups.Add(constructGroup);

            if (result.Groups.Count == 0)
            {
                Debug.LogWarning("[TsvrcCompiler] All arrays are empty. Nothing to generate.");
                return null;
            }

            return result;
        }
    }
}
#endif
