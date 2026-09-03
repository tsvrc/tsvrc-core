using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Player;
using Tsvrc.Testing.Framework;
using UnityEngine;

namespace Tsvrc.Tests.EditMode
{
    // PlayerColorAssigner is a pure function of a player's numeric id modulo the palette.
    // Covers that property, the palette's distinctness, and that Initialize reorders it.
    public class PlayerColorAssignerTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private PlayerColorAssigner CreateAssigner()
        {
            var go = new GameObject("Assigner");
            _spawned.Add(go);
            return go.AddComponent<PlayerColorAssigner>();
        }

        private static Color[] GetPalette(PlayerColorAssigner assigner)
        {
            return PrivateFieldAccess.GetField<Color[]>(assigner, "_palette");
        }

        private static byte[] GetOrder(PlayerColorAssigner assigner)
        {
            return PrivateFieldAccess.GetField<byte[]>(assigner, "_order");
        }

        [Test]
        public void Palette_Has82Colors_AllDistinct()
        {
            Color[] palette = GetPalette(CreateAssigner());

            Assert.AreEqual(82, palette.Length,
                "The palette must have one slot per player VRChat's instance hard cap allows.");

            var seen = new HashSet<Color>();
            foreach (Color c in palette)
                Assert.IsTrue(seen.Add(c), $"Duplicate color {c} found in the palette.");
        }

        [Test]
        public void GetColor_SamePlayerId_ReturnsSameColorConsistently()
        {
            var assigner = CreateAssigner();

            Color first = assigner.GetColor("Alice#7");
            Color second = assigner.GetColor("Alice#7");

            Assert.AreEqual(first, second);
        }

        [Test]
        public void GetColor_DifferentNumericIds_MapToDifferentPaletteSlots()
        {
            var assigner = CreateAssigner();
            Color[] palette = GetPalette(assigner);

            Assert.AreEqual(palette[7], assigner.GetColor("Alice#7"));
            Assert.AreEqual(palette[8], assigner.GetColor("Bob#8"));
        }

        [Test]
        public void GetColor_NumericIdBeyondPaletteLength_WrapsAroundModulo()
        {
            var assigner = CreateAssigner();
            Color[] palette = GetPalette(assigner);

            // 82 + 5 wraps to the same slot as 5 - ids can exceed the palette length over a
            // long-running instance even though concurrent players never do.
            Assert.AreEqual(palette[5], assigner.GetColor("Carol#87"));
        }

        [Test]
        public void GetColor_MalformedPlayerId_ReturnsSlotZeroWithoutThrowing()
        {
            var assigner = CreateAssigner();
            Color[] palette = GetPalette(assigner);

            Color result = default;
            Assert.DoesNotThrow(() => result = assigner.GetColor("nobody-with-no-hash"));
            Assert.AreEqual(palette[0], result);
        }

        [Test]
        public void GetColor_BeforeInitialize_UsesIdentityOrder()
        {
            var assigner = CreateAssigner();
            Color[] palette = GetPalette(assigner);

            Assert.AreEqual(palette[7], assigner.GetColor("Alice#7"));
        }

        [Test]
        public void Initialize_ShufflesOrder_SamePaletteIndicesDifferentPositions()
        {
            var assigner = CreateAssigner();

            assigner.Initialize();
            byte[] order = GetOrder(assigner);

            Assert.AreEqual(82, order.Length);
            var seen = new HashSet<byte>(order);
            Assert.AreEqual(82, seen.Count, "Order must be a permutation of all 82 palette indices.");

            bool anyPositionChanged = false;
            for (int i = 0; i < order.Length; i++)
            {
                if (order[i] != i)
                {
                    anyPositionChanged = true;
                    break;
                }
            }
            Assert.IsTrue(anyPositionChanged,
                "A shuffle landing back in its original order is astronomically unlikely (1 in 82!) - " +
                "this failing means Initialize isn't shuffling.");
        }

        [Test]
        public void GetColor_AfterInitialize_ResolvesThroughOrderIndirection()
        {
            var assigner = CreateAssigner();
            assigner.Initialize();
            Color[] palette = GetPalette(assigner);
            byte[] order = GetOrder(assigner);

            Assert.AreEqual(palette[order[7]], assigner.GetColor("Alice#7"));
        }

        [Test]
        public void Initialize_CalledTwiceOnOwner_ReshufflesAgain()
        {
            // A bare EditMode GameObject is unconditionally its own Unity owner, so this
            // exercises the owner path both times.
            var assigner = CreateAssigner();
            assigner.Initialize();
            byte[] first = (byte[])GetOrder(assigner).Clone();

            // Re-roll until a different permutation actually comes up - Random.Range can
            // legitimately reproduce the same shuffle by chance on a single retry.
            byte[] second = null;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                assigner.Initialize();
                second = GetOrder(assigner);
                if (!System.Linq.Enumerable.SequenceEqual(first, second)) break;
            }

            CollectionAssert.AreNotEqual(first, second,
                "Calling Initialize again as the owner must reshuffle, not leave the order untouched.");
        }
    }
}
