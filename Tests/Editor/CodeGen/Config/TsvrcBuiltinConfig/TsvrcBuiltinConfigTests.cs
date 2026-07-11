using System.Reflection;
using NUnit.Framework;
using Tsvrc.Editor;
using UnityEngine;

namespace Tsvrc.Tests.Editor
{
    // TsvrcBuiltinConfig is a pure data container with no methods, used as a config
    // fixture across the CodeGen test suite. SingletonModule generates its Singletons
    // entries directly as fields on the TsvrcGenerated partial class, with no
    // intermediate wrapper type.
    public class TsvrcBuiltinConfigTests
    {
        [Test]
        public void Singletons_TooltipText_NamesTheRealGeneratedClass_NotAStaleApiShape()
        {
            FieldInfo field = typeof(TsvrcBuiltinConfig).GetField(nameof(TsvrcBuiltinConfig.Singletons));
            var tooltip = field.GetCustomAttribute<TooltipAttribute>();

            Assert.IsNotNull(tooltip);
            StringAssert.DoesNotContain("TsvrcSingletonBehaviour", tooltip.tooltip);
            StringAssert.Contains("TsvrcGenerated", tooltip.tooltip);
        }

        [Test]
        public void FreshInstance_AllThreeFields_DefaultToNull()
        {
            var config = ScriptableObject.CreateInstance<TsvrcBuiltinConfig>();

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
