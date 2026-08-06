using NUnit.Framework;
using Tsvrc.Editor;

namespace Tsvrc.Tests.EditMode
{
    // TsGenerator.CreateModules() - the fixed module list/order used both by Run() and
    // by TsWindow to build its tab list.
    public class TsGeneratorCreateModulesTests
    {
        [Test]
        public void CreateModules_ReturnsAllNineModulesInDocumentedOrder()
        {
            var modules = TsGenerator.CreateModules();

            Assert.AreEqual(9, modules.Count);
            Assert.IsInstanceOf<LogModule>(modules[0]);
            Assert.IsInstanceOf<MemoryModule>(modules[1]);
            Assert.IsInstanceOf<PoolModule>(modules[2]);
            Assert.IsInstanceOf<TranslationModule>(modules[3]);
            Assert.IsInstanceOf<InstanceModule>(modules[4]);
            Assert.IsInstanceOf<GlobalModule>(modules[5]);
            Assert.IsInstanceOf<ConstructModule>(modules[6]);
            Assert.IsInstanceOf<FactoryModule>(modules[7]);
            Assert.IsInstanceOf<ScaffoldModule>(modules[8]);
        }

        [Test]
        public void CreateModules_CalledTwice_ReturnsFreshDistinctInstancesEachTime()
        {
            // Each Run() pass must get its own modules, never shared/reused state across runs.
            var first = TsGenerator.CreateModules();
            var second = TsGenerator.CreateModules();

            Assert.AreNotSame(first[0], second[0]);
        }
    }
}
