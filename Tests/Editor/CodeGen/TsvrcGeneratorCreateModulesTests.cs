using NUnit.Framework;
using Tsvrc.Editor;

namespace Tsvrc.Tests.Editor
{
    // TsvrcGenerator.CreateModules() - the fixed module list/order used both by Run() and
    // by TsvrcWindow to build its tab list. Phase G6 (orchestration, safe subset - no real
    // Run() needed).
    public class TsvrcGeneratorCreateModulesTests
    {
        [Test]
        public void CreateModules_ReturnsAllEightModulesInDocumentedOrder()
        {
            var modules = TsvrcGenerator.CreateModules();

            Assert.AreEqual(8, modules.Count);
            Assert.IsInstanceOf<MemoryModule>(modules[0]);
            Assert.IsInstanceOf<PoolModule>(modules[1]);
            Assert.IsInstanceOf<TranslationModule>(modules[2]);
            Assert.IsInstanceOf<InstanceModule>(modules[3]);
            Assert.IsInstanceOf<SingletonModule>(modules[4]);
            Assert.IsInstanceOf<ConstructModule>(modules[5]);
            Assert.IsInstanceOf<FactoryModule>(modules[6]);
            Assert.IsInstanceOf<ScaffoldModule>(modules[7]);
        }

        [Test]
        public void CreateModules_CalledTwice_ReturnsFreshDistinctInstancesEachTime()
        {
            // Each Run() pass must get its own modules, never shared/reused state across runs.
            var first = TsvrcGenerator.CreateModules();
            var second = TsvrcGenerator.CreateModules();

            Assert.AreNotSame(first[0], second[0]);
        }
    }
}
