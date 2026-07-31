using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Utils;
using UnityEngine;
using VRC.SDK3.Data;

namespace Tsvrc.Tests.EditMode
{
    public class TsMemoryReadTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private TsvrcMemory CreateMemory()
        {
            var go = new GameObject(nameof(TsvrcMemory));
            _spawned.Add(go);
            return go.AddComponent<TsvrcMemory>();
        }

        [Test]
        public void Has_KeyPresent_ReturnsTrue()
        {
            TsvrcMemory memory = CreateMemory();
            memory.Set("k", new DataToken("v"));

            Assert.IsTrue(memory.Has("k"));
        }

        [Test]
        public void Has_KeyAbsent_ReturnsFalse()
        {
            TsvrcMemory memory = CreateMemory();

            Assert.IsFalse(memory.Has("k"));
        }

        [Test]
        public void Get_ReturnsRawToken()
        {
            TsvrcMemory memory = CreateMemory();
            memory.Set("k", new DataToken(42));

            DataToken token = memory.Get("k");

            Assert.AreEqual(TokenType.Int, token.TokenType);
            Assert.AreEqual(42, token.Int);
        }

        [Test]
        public void Get_MissingKey_ReturnsErrorToken()
        {
            TsvrcMemory memory = CreateMemory();

            DataToken token = memory.Get("missing");

            Assert.AreEqual(TokenType.Error, token.TokenType);
        }

        [Test]
        public void Has_NullKey_ReturnsFalseWithoutThrowing()
        {
            TsvrcMemory memory = CreateMemory();

            Assert.DoesNotThrow(() => memory.Has(null));
            Assert.IsFalse(memory.Has(null));
        }

        [Test]
        public void GetString_MissingKey_ReturnsNullWithoutThrowing()
        {
            // Asymmetric with GetBool/GetInt/GetFloat/GetDict/GetList below: DataToken.String
            // tolerates an Error-typed token (falls back to null) where every other typed
            // property throws InvalidOperationException for the same input.
            TsvrcMemory memory = CreateMemory();

            string result = "not null";
            Assert.DoesNotThrow(() => result = memory.GetString("missing"));
            Assert.IsNull(result);
        }

        [Test]
        public void GetBool_MissingKey_ThrowsInvalidOperationException()
        {
            TsvrcMemory memory = CreateMemory();

            Assert.Throws<System.InvalidOperationException>(() => memory.GetBool("missing"));
        }

        [Test]
        public void GetInt_MissingKey_ThrowsInvalidOperationException()
        {
            TsvrcMemory memory = CreateMemory();

            Assert.Throws<System.InvalidOperationException>(() => memory.GetInt("missing"));
        }

        [Test]
        public void GetFloat_MissingKey_ThrowsInvalidOperationException()
        {
            TsvrcMemory memory = CreateMemory();

            Assert.Throws<System.InvalidOperationException>(() => memory.GetFloat("missing"));
        }

        [Test]
        public void GetDict_MissingKey_ThrowsInvalidOperationException()
        {
            TsvrcMemory memory = CreateMemory();

            Assert.Throws<System.InvalidOperationException>(() => memory.GetDict("missing"));
        }

        [Test]
        public void GetList_MissingKey_ThrowsInvalidOperationException()
        {
            TsvrcMemory memory = CreateMemory();

            Assert.Throws<System.InvalidOperationException>(() => memory.GetList("missing"));
        }

        [Test]
        public void GetString_RoundTrips()
        {
            TsvrcMemory memory = CreateMemory();
            memory.Set("k", new DataToken("hello"));

            Assert.AreEqual("hello", memory.GetString("k"));
        }

        [Test]
        public void GetBool_RoundTrips()
        {
            TsvrcMemory memory = CreateMemory();
            memory.Set("k", new DataToken(true));

            Assert.IsTrue(memory.GetBool("k"));
        }

        [Test]
        public void GetDict_RoundTrips()
        {
            TsvrcMemory memory = CreateMemory();
            var nested = new DataDictionary();
            nested["inner"] = new DataToken("value");
            memory.Set("k", new DataToken(nested));

            DataDictionary result = memory.GetDict("k");

            Assert.AreEqual("value", result["inner"].String);
        }

        [Test]
        public void GetList_RoundTrips()
        {
            TsvrcMemory memory = CreateMemory();
            var list = new DataList();
            list.Add(new DataToken("a"));
            list.Add(new DataToken("b"));
            memory.Set("k", new DataToken(list));

            DataList result = memory.GetList("k");

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("a", result[0].String);
        }

        [Test]
        public void GetInt_NativeIntToken_ReturnsExactValue()
        {
            TsvrcMemory memory = CreateMemory();
            memory.Set("k", new DataToken(7));

            Assert.AreEqual(7, memory.GetInt("k"));
        }

        [Test]
        public void GetInt_DoubleToken_CoercesToInt()
        {
            // Simulates the post-JSON/PlayerData round-trip case where an integer literal
            // comes back typed as Double rather than Int.
            TsvrcMemory memory = CreateMemory();
            memory.Set("k", new DataToken(7.0d));

            Assert.AreEqual(TokenType.Double, memory.Get("k").TokenType);
            Assert.AreEqual(7, memory.GetInt("k"));
        }

        [Test]
        public void GetFloat_NativeFloatToken_ReturnsExactValue()
        {
            TsvrcMemory memory = CreateMemory();
            memory.Set("k", new DataToken(3.5f));

            Assert.AreEqual(3.5f, memory.GetFloat("k"));
        }

        [Test]
        public void GetFloat_DoubleToken_CoercesToFloat()
        {
            TsvrcMemory memory = CreateMemory();
            memory.Set("k", new DataToken(3.5d));

            Assert.AreEqual(TokenType.Double, memory.Get("k").TokenType);
            Assert.AreEqual(3.5f, memory.GetFloat("k"));
        }

        [Test]
        public void GetInt_PersistTierDoubleToken_CoercesToInt()
        {
            // The Double-coercion tests above only exercise the default ephemeral tier;
            // this proves GetInt's coercion applies identically once routed through
            // _Store(flags) to the persist tier.
            TsvrcMemory memory = CreateMemory();
            memory.Register("k", true, false);
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, "[TsVRC] [TsvrcMemory] Set('k') before OnPlayerRestored. Value will not be saved to PlayerData.");
            memory.Set("k", new DataToken(9.0d));

            Assert.AreEqual(9, memory.GetInt("k"));
        }

        [Test]
        public void GetFloat_SyncedTierDoubleToken_CoercesToFloat()
        {
            TsvrcMemory memory = CreateMemory();
            memory.Register("k", false, true);
            memory.Set("k", new DataToken(2.25d));

            Assert.AreEqual(2.25f, memory.GetFloat("k"));
        }

        [Test]
        public void Has_SeparatesTiersByRegistration()
        {
            TsvrcMemory memory = CreateMemory();
            memory.Set("ephemeralKey", new DataToken("e"));
            memory.Register("persistKey", true, false);
            memory.Register("syncedKey", false, true);

            Assert.IsFalse(memory.Has("persistKey"));
            Assert.IsFalse(memory.Has("syncedKey"));
            Assert.IsTrue(memory.Has("ephemeralKey"));
        }

        [Test]
        public void GetString_PersistTierValue_RoundTrips()
        {
            TsvrcMemory memory = CreateMemory();
            memory.Register("k", true, false);
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, "[TsVRC] [TsvrcMemory] Set('k') before OnPlayerRestored. Value will not be saved to PlayerData.");
            memory.Set("k", new DataToken("v"));

            Assert.AreEqual("v", memory.GetString("k"));
        }

        [Test]
        public void GetString_SyncedTierValue_RoundTrips()
        {
            TsvrcMemory memory = CreateMemory();
            memory.Register("k", false, true);
            memory.Set("k", new DataToken("v"));

            Assert.AreEqual("v", memory.GetString("k"));
        }
    }
}
