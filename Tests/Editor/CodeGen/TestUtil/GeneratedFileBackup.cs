using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Tsvrc.Tests.Editor
{
    // Backs up and byte-for-byte restores Assets/TsvrcGenerated/*.cs around any test that
    // drives the real, static, project-wide TsvrcGenerator.Run() - which really does write
    // those files to disk (WriteIfChanged is a genuine file write, not scratch). Without
    // this, a test running Run() against an empty synthetic scene/config could silently
    // overwrite a developer's actual configured output with "stub" content.
    //
    // Uses raw bytes (not ReadAllText/WriteAllText) so the original UTF-8 BOM is preserved
    // exactly - WriteAllText without an explicit Encoding.UTF8 writes no-BOM UTF-8 by
    // default, which would otherwise leave a spurious BOM-only diff on every restore.
    internal sealed class GeneratedFileBackup : IDisposable
    {
        private const string GeneratedFolder = "Assets/TsvrcGenerated";

        private static readonly string[] GeneratedFileNames =
        {
            "TsvrcGenerated.cs", "TsvrcGeneratedConstruct.cs", "TsvrcGeneratedFactory.cs",
            "TsvrcGeneratedInstance.cs", "TsvrcGeneratedMemory.cs", "TsvrcGeneratedPool.cs",
            "TsvrcGeneratedSingleton.cs", "TsvrcGeneratedTranslation.cs",
        };

        private readonly Dictionary<string, byte[]> _backup = new Dictionary<string, byte[]>();

        internal GeneratedFileBackup()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            foreach (var fileName in GeneratedFileNames)
            {
                string path = Path.Combine(projectRoot, GeneratedFolder.Replace('/', Path.DirectorySeparatorChar), fileName);
                if (File.Exists(path)) _backup[path] = File.ReadAllBytes(path);
            }
        }

        public void Dispose()
        {
            foreach (var pair in _backup)
                File.WriteAllBytes(pair.Key, pair.Value);
        }
    }
}
