using System.Reflection;
using NUnit.Framework;
using Tsvrc.Editor;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // TsBuiltinConfig is a pure data container with no methods, used as a config
    // fixture across the CodeGen test suite. SingletonModule generates its Singletons
    // entries directly as fields on the TsGenerated partial class, with no
    // intermediate wrapper type.
    public class TsBuiltinConfigTests
    {
        [Test]
        public void Singletons_TooltipText_NamesTheRealGeneratedClass_NotAStaleApiShape()
        {
            FieldInfo field = typeof(TsBuiltinConfig).GetField(nameof(TsBuiltinConfig.Singletons));
            var tooltip = field.GetCustomAttribute<TooltipAttribute>();

            Assert.IsNotNull(tooltip);
            StringAssert.DoesNotContain("TsSingletonBehaviour", tooltip.tooltip);
            StringAssert.Contains("TsGenerated", tooltip.tooltip);
        }

        [Test]
        public void FreshInstance_AllThreeFields_DefaultToNull()
        {
            var config = ScriptableObject.CreateInstance<TsBuiltinConfig>();

            try
            {
                Assert.IsNull(config.Singletons);
                Assert.IsNull(config.PoolPrefabs);
                Assert.IsNull(config.Factories);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }
    }
}
