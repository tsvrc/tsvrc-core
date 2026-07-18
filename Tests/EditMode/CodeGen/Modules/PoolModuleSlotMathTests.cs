using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Tsvrc.Editor;
using UnityEngine.TestTools;

namespace Tsvrc.Tests.EditMode
{
    // PoolModule's slot-count graph algorithm (ComputeTotalSlots/ComputeForType) is pure
    // graph math over private instance state (_poolTypeInfos, a Dictionary<string,
    // PoolTypeInfo> where PoolTypeInfo is itself a private nested class). Neither is
    // reachable via InternalsVisibleTo (that only grants `internal`, not `private`), so
    // this harness builds the private graph via reflection, runs the real algorithm, and
    // reads back each node's computed TotalSlots.
    public class PoolModuleSlotMathTests
    {
        private struct PoolNode
        {
            public string Name;
            public int ExternalCount;
            public (string Dep, int Count)[] Deps;
        }

        private static readonly Type ModuleType = typeof(PoolModule);
        private static readonly Type InfoType = ModuleType.GetNestedType("PoolTypeInfo", BindingFlags.NonPublic);
        private static readonly FieldInfo PoolTypeInfosField = ModuleType.GetField("_poolTypeInfos", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo ComputeTotalSlotsMethod = ModuleType.GetMethod("ComputeTotalSlots", BindingFlags.NonPublic | BindingFlags.Instance);

        private static Dictionary<string, int> ComputeSlots(params PoolNode[] nodes)
        {
            Assert.IsNotNull(InfoType, "PoolModule.PoolTypeInfo nested type changed or was removed.");
            Assert.IsNotNull(PoolTypeInfosField, "PoolModule._poolTypeInfos field changed or was removed.");
            Assert.IsNotNull(ComputeTotalSlotsMethod, "PoolModule.ComputeTotalSlots method changed or was removed.");

            FieldInfo typeNameField = InfoType.GetField("TypeName");
            FieldInfo externalCountField = InfoType.GetField("ExternalCount");
            FieldInfo internalDepsField = InfoType.GetField("InternalDeps");
            FieldInfo totalSlotsField = InfoType.GetField("TotalSlots");

            var infosByName = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var node in nodes)
            {
                object info = Activator.CreateInstance(InfoType);
                typeNameField.SetValue(info, node.Name);
                externalCountField.SetValue(info, node.ExternalCount);

                var deps = (IDictionary)internalDepsField.GetValue(info);
                foreach (var (dep, count) in node.Deps)
                    deps[dep] = count;

                infosByName[node.Name] = info;
            }

            Type dictType = typeof(Dictionary<,>).MakeGenericType(typeof(string), InfoType);
            var typedDict = (IDictionary)Activator.CreateInstance(dictType, StringComparer.Ordinal);
            foreach (var kv in infosByName)
                typedDict[kv.Key] = kv.Value;

            object moduleInstance = new PoolModule();
            PoolTypeInfosField.SetValue(moduleInstance, typedDict);
            ComputeTotalSlotsMethod.Invoke(moduleInstance, null);

            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var kv in infosByName)
                result[kv.Key] = (int)totalSlotsField.GetValue(kv.Value);
            return result;
        }

        [Test]
        public void ComputeTotalSlots_NoExternalAndNoInternalRefs_IsZero()
        {
            var result = ComputeSlots(new PoolNode { Name = "Isolated", ExternalCount = 0, Deps = Array.Empty<(string, int)>() });

            Assert.AreEqual(0, result["Isolated"]);
        }

        [Test]
        public void ComputeTotalSlots_ExternalRefsOnly_EqualsExternalCount()
        {
            var result = ComputeSlots(new PoolNode { Name = "Leaf", ExternalCount = 3, Deps = Array.Empty<(string, int)>() });

            Assert.AreEqual(3, result["Leaf"]);
        }

        [Test]
        public void ComputeTotalSlots_ParentChild_ChildSlotsScaleWithParentInstances()
        {
            // X has 2 external refs (2 X instances will exist) and each X contains 3
            // [WirePool] Y fields, so Y needs 3 * 2 = 6 slots even though Y has no
            // external refs of its own.
            var result = ComputeSlots(
                new PoolNode { Name = "X", ExternalCount = 2, Deps = new (string, int)[] { ("Y", 3) } },
                new PoolNode { Name = "Y", ExternalCount = 0, Deps = Array.Empty<(string, int)>() });

            Assert.AreEqual(2, result["X"]);
            Assert.AreEqual(6, result["Y"]);
        }

        [Test]
        public void ComputeTotalSlots_DiamondGraph_SharedNodeSumsContributionsFromBothParents()
        {
            // Top has 1 external ref (1 instance), containing 1 Left + 1 Right field each.
            // Left contains 2 Shared fields, Right contains 3 Shared fields — Shared must
            // sum both paths: (2 * Left.TotalSlots) + (3 * Right.TotalSlots).
            var result = ComputeSlots(
                new PoolNode { Name = "Top", ExternalCount = 1, Deps = new (string, int)[] { ("Left", 1), ("Right", 1) } },
                new PoolNode { Name = "Left", ExternalCount = 0, Deps = new (string, int)[] { ("Shared", 2) } },
                new PoolNode { Name = "Right", ExternalCount = 0, Deps = new (string, int)[] { ("Shared", 3) } },
                new PoolNode { Name = "Shared", ExternalCount = 0, Deps = Array.Empty<(string, int)>() });

            Assert.AreEqual(1, result["Top"]);
            Assert.AreEqual(1, result["Left"]);
            Assert.AreEqual(1, result["Right"]);
            Assert.AreEqual(5, result["Shared"], "Shared should sum contributions from both Left (2*1) and Right (3*1).");
        }

        [Test]
        public void ComputeTotalSlots_SelfReferencingType_LogsErrorAndTreatsCycleEdgeAsZero()
        {
            // A contains 2 [WirePool] fields of its own type. The DFS detects the cycle
            // when it re-enters "A" while already resolving "A" (inStack), logs an error,
            // and substitutes 0 for that one recursive contribution — it does not hang and
            // does not throw. The surviving total is just A's own external count, since the
            // only other contribution (the self-referencing edge) resolved to 0.
            LogAssert.Expect(UnityEngine.LogType.Error,
                "[PoolModule] Circular dependency detected involving 'A'. Excluding from pool generation.");

            Dictionary<string, int> result = null;
            Assert.DoesNotThrow(() =>
            {
                result = ComputeSlots(
                    new PoolNode { Name = "A", ExternalCount = 4, Deps = new (string, int)[] { ("A", 2) } });
            });

            Assert.AreEqual(4, result["A"]);
        }

        [Test]
        public void ComputeTotalSlots_ThreeLevelChain_ResolvesDepthGreaterThanTwo()
        {
            // C (the only externally-referenced type, 5 scene instances) contains 1
            // [WirePool] field of B; each B in turn contains 2 [WirePool] fields of A.
            // Slots flow from the external anchor down through the chain: B = 1*C.Total,
            // A = 2*B.Total. Confirms the topological DFS isn't accidentally limited to a
            // single hop (a bug here would resolve B correctly but leave A at 0).
            var result = ComputeSlots(
                new PoolNode { Name = "C", ExternalCount = 5, Deps = new (string, int)[] { ("B", 1) } },
                new PoolNode { Name = "B", ExternalCount = 0, Deps = new (string, int)[] { ("A", 2) } },
                new PoolNode { Name = "A", ExternalCount = 0, Deps = Array.Empty<(string, int)>() });

            Assert.AreEqual(5, result["C"]);
            Assert.AreEqual(5, result["B"], "B = 1 field-in-C * C.TotalSlots (1*5).");
            Assert.AreEqual(10, result["A"], "A = 2 fields-in-B * B.TotalSlots (2*5).");
        }

        [Test]
        public void ComputeTotalSlots_DisconnectedForest_DoesNotCrossContaminate()
        {
            // Two entirely unrelated graphs computed within the same _poolTypeInfos
            // dictionary/single ComputeTotalSlots() pass must not affect each other's totals.
            var result = ComputeSlots(
                new PoolNode { Name = "X", ExternalCount = 3, Deps = new (string, int)[] { ("Y", 2) } },
                new PoolNode { Name = "Y", ExternalCount = 0, Deps = Array.Empty<(string, int)>() },
                new PoolNode { Name = "M", ExternalCount = 7, Deps = Array.Empty<(string, int)>() },
                new PoolNode { Name = "N", ExternalCount = 0, Deps = new (string, int)[] { ("M", 4) } });

            Assert.AreEqual(3, result["X"]);
            Assert.AreEqual(6, result["Y"], "Y = 2 * X.TotalSlots (2*3).");
            Assert.AreEqual(7, result["M"]);
            Assert.AreEqual(0, result["N"], "N has no external refs and nothing points at N.");
        }
    }
}
