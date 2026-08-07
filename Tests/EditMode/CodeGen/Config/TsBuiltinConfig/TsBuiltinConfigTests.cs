using System.Reflection;
using NUnit.Framework;
using Tsvrc.Editor;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // TsBuiltinConfig is a pure data container with no methods, used as a config
    // fixture across the CodeGen test suite. GlobalModule generates its Globals
    // entries directly as fields on the TsGenerated partial class, with no
    // intermediate wrapper type.
    public class TsBuiltinConfigTests
    {
        [Test]
        public void GlobalEntries_TooltipText_NamesTheRealGeneratedClass_NotAStaleApiShape()
        {
            FieldInfo field = typeof(TsBuiltinConfig).GetField(nameof(TsBuiltinConfig.GlobalEntries));
            var tooltip = field.GetCustomAttribute<TooltipAttribute>();

            Assert.IsNotNull(tooltip);
            StringAssert.DoesNotContain("TsGlobalBehaviour", tooltip.tooltip);
            StringAssert.Contains("TsGenerated", tooltip.tooltip);
        }

        [Test]
        public void FreshInstance_ArrayFields_DefaultToNull()
        {
            var config = ScriptableObject.CreateInstance<TsBuiltinConfig>();

            try
            {
                Assert.IsNull(config.GlobalEntries);
                Assert.IsNull(config.GlobalGroups);
                Assert.IsNull(config.PoolEntries);
                Assert.IsNull(config.PoolGroups);
                Assert.IsNull(config.FactoryEntries);
                Assert.IsNull(config.FactoryGroups);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }
    }
}
