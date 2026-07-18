using NUnit.Framework;
using Tsvrc.Utils;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.Data;

namespace Tsvrc.Tests.EditMode
{
    public class TsJsonTests
    {
        [Test]
        public void Serialize_SimpleDictionary_ProducesParseableJson()
        {
            var dict = new DataDictionary();
            dict.Add("key", new DataToken("value"));
            dict.Add("num", new DataToken(5d));

            string json = TsJson.Serialize(dict);

            Assert.IsFalse(string.IsNullOrEmpty(json));
            StringAssert.Contains("\"key\"", json);
            StringAssert.Contains("\"value\"", json);
        }

        [Test]
        public void Serialize_NullDictionary_LogsErrorAndReturnsEmptyString()
        {
            LogAssert.Expect(LogType.Error, "[TsJson] Failed to serialize DataDictionary to JSON.");

            string json = TsJson.Serialize(null);

            Assert.AreEqual(string.Empty, json);
        }

        [Test]
        public void Serialize_EmptyDictionary_ProducesEmptyObjectJson()
        {
            string json = TsJson.Serialize(new DataDictionary());

            Assert.AreEqual("{}", json);
        }

        [Test]
        public void Deserialize_ValidObjectJson_ReturnsMatchingDictionary()
        {
            DataDictionary result = TsJson.Deserialize("{\"key\":\"value\",\"num\":5}");

            Assert.IsNotNull(result);
            Assert.AreEqual("value", result["key"].String);
            Assert.AreEqual(5d, result["num"].Double);
        }

        [Test]
        public void SerializeThenDeserialize_RoundTrips()
        {
            var dict = new DataDictionary();
            dict.Add("a", new DataToken(1d));
            dict.Add("b", new DataToken("text"));

            DataDictionary result = TsJson.Deserialize(TsJson.Serialize(dict));

            Assert.AreEqual(1d, result["a"].Double);
            Assert.AreEqual("text", result["b"].String);
        }

        [Test]
        public void SerializeThenDeserialize_BooleanValue_RoundTrips()
        {
            var dict = new DataDictionary();
            dict.Add("flag", new DataToken(true));
            dict.Add("flag2", new DataToken(false));

            DataDictionary result = TsJson.Deserialize(TsJson.Serialize(dict));

            Assert.AreEqual(TokenType.Boolean, result["flag"].TokenType);
            Assert.IsTrue(result["flag"].Boolean);
            Assert.IsFalse(result["flag2"].Boolean);
        }

        [Test]
        public void SerializeThenDeserialize_NegativeAndDecimalNumbers_RoundTrip()
        {
            var dict = new DataDictionary();
            dict.Add("neg", new DataToken(-42d));
            dict.Add("decimal", new DataToken(3.14159d));

            DataDictionary result = TsJson.Deserialize(TsJson.Serialize(dict));

            Assert.AreEqual(-42d, result["neg"].Double);
            Assert.AreEqual(3.14159d, result["decimal"].Double, 0.00001);
        }

        [Test]
        public void SerializeThenDeserialize_StringWithSpecialCharacters_RoundTrips()
        {
            const string tricky = "quote\"back\\slash\nnewline\ttabéunicode";
            var dict = new DataDictionary();
            dict.Add("tricky", new DataToken(tricky));

            DataDictionary result = TsJson.Deserialize(TsJson.Serialize(dict));

            Assert.AreEqual(tricky, result["tricky"].String);
        }

        [Test]
        public void Deserialize_NullString_LogsErrorAndReturnsNull()
        {
            LogAssert.Expect(LogType.Error, "[TsJson] Cannot deserialize null or empty JSON string.");

            DataDictionary result = TsJson.Deserialize(null);

            Assert.IsNull(result);
        }

        [Test]
        public void Deserialize_EmptyString_LogsErrorAndReturnsNull()
        {
            LogAssert.Expect(LogType.Error, "[TsJson] Cannot deserialize null or empty JSON string.");

            DataDictionary result = TsJson.Deserialize(string.Empty);

            Assert.IsNull(result);
        }

        [Test]
        public void Deserialize_MalformedJson_LogsErrorAndReturnsNull()
        {
            LogAssert.Expect(LogType.Error, "[TsJson] Failed to deserialize JSON string to DataDictionary.");

            DataDictionary result = TsJson.Deserialize("{not valid json");

            Assert.IsNull(result);
        }

        [Test]
        public void Deserialize_ValidJsonButNotADictionary_LogsErrorAndReturnsNull()
        {
            LogAssert.Expect(LogType.Error, "[TsJson] Deserialized JSON is not a DataDictionary.");

            DataDictionary result = TsJson.Deserialize("[1,2,3]");

            Assert.IsNull(result);
        }

        [Test]
        public void Deserialize_BareNumberLiteral_NotValidTopLevelJson_LogsErrorAndReturnsNull()
        {
            // VRCJson requires an object or array at the top level; a bare scalar fails to
            // parse at all (VRCJson.TryDeserializeFromJson returns false), rather than
            // succeeding with a non-dictionary token.
            LogAssert.Expect(LogType.Error, "[TsJson] Failed to deserialize JSON string to DataDictionary.");

            DataDictionary result = TsJson.Deserialize("5");

            Assert.IsNull(result);
        }

        [Test]
        public void Deserialize_WhitespaceOnlyString_IsNotTreatedAsEmpty_FailsToParseInstead()
        {
            // string.IsNullOrEmpty(" ") is false, so whitespace-only input skips the
            // "cannot deserialize null or empty" guard and falls through to VRCJson,
            // which fails to parse it — a different error message than the empty-string case.
            LogAssert.Expect(LogType.Error, "[TsJson] Failed to deserialize JSON string to DataDictionary.");

            DataDictionary result = TsJson.Deserialize("   ");

            Assert.IsNull(result);
        }

        [Test]
        public void SerializeToken_DictionaryToken_ProducesParseableJson()
        {
            var dict = new DataDictionary();
            dict.Add("key", new DataToken("value"));

            string json = TsJson.SerializeToken(new DataToken(dict));

            StringAssert.Contains("\"key\"", json);
        }

        [Test]
        public void SerializeToken_ListToken_ProducesParseableJson()
        {
            var list = new DataList();
            list.Add(new DataToken(1d));
            list.Add(new DataToken(2d));

            string json = TsJson.SerializeToken(new DataToken(list));

            StringAssert.Contains("[1,2]".Replace(" ", ""), json.Replace(" ", ""));
        }

        [Test]
        public void DeserializeToken_ValidListJson_ReturnsListToken()
        {
            DataToken token = TsJson.DeserializeToken("[1,2,3]");

            Assert.AreEqual(TokenType.DataList, token.TokenType);
            Assert.AreEqual(3, token.DataList.Count);
        }

        [Test]
        public void DeserializeToken_BareNumberLiteral_NotValidTopLevelJson_LogsErrorAndReturnsDefault()
        {
            // Same VRCJson top-level restriction as Deserialize: a bare scalar is not
            // accepted at the top level, so this fails to parse entirely.
            LogAssert.Expect(LogType.Error, "[TsJson] Failed to deserialize JSON string.");

            DataToken token = TsJson.DeserializeToken("5");

            Assert.AreEqual(default(DataToken).TokenType, token.TokenType);
        }

        [Test]
        public void DeserializeToken_ArrayWithNumbers_ReturnsListOfDoubleTokens()
        {
            DataToken token = TsJson.DeserializeToken("[5,6]");

            Assert.AreEqual(TokenType.DataList, token.TokenType);
            Assert.AreEqual(TokenType.Double, token.DataList[0].TokenType);
            Assert.AreEqual(5d, token.DataList[0].Double);
        }

        [Test]
        public void DeserializeToken_ValidObjectJson_ReturnsDataDictionaryToken()
        {
            DataToken token = TsJson.DeserializeToken("{\"key\":\"value\"}");

            Assert.AreEqual(TokenType.DataDictionary, token.TokenType);
            Assert.AreEqual("value", token.DataDictionary["key"].String);
        }

        [Test]
        public void SerializeToken_EmptyDataList_ProducesEmptyArrayJson()
        {
            string json = TsJson.SerializeToken(new DataToken(new DataList()));

            Assert.AreEqual("[]", json);
        }

        [Test]
        public void SerializeTokenThenDeserializeToken_RoundTrips()
        {
            var list = new DataList();
            list.Add(new DataToken("x"));

            DataToken result = TsJson.DeserializeToken(TsJson.SerializeToken(new DataToken(list)));

            Assert.AreEqual(TokenType.DataList, result.TokenType);
            Assert.AreEqual("x", result.DataList[0].String);
        }

        [Test]
        public void DeserializeToken_NullString_LogsErrorAndReturnsDefaultToken()
        {
            LogAssert.Expect(LogType.Error, "[TsJson] Cannot deserialize null or empty JSON string.");

            DataToken token = TsJson.DeserializeToken(null);

            Assert.AreEqual(default(DataToken).TokenType, token.TokenType);
        }

        [Test]
        public void DeserializeToken_EmptyString_LogsErrorAndReturnsDefaultToken()
        {
            LogAssert.Expect(LogType.Error, "[TsJson] Cannot deserialize null or empty JSON string.");

            DataToken token = TsJson.DeserializeToken(string.Empty);

            Assert.AreEqual(default(DataToken).TokenType, token.TokenType);
        }

        [Test]
        public void DeserializeToken_MalformedJson_LogsErrorAndReturnsDefaultToken()
        {
            LogAssert.Expect(LogType.Error, "[TsJson] Failed to deserialize JSON string.");

            DataToken token = TsJson.DeserializeToken("{not valid");

            Assert.AreEqual(default(DataToken).TokenType, token.TokenType);
        }

        [Test]
        public void Clone_SimpleDictionary_ProducesEqualButIndependentCopy()
        {
            var original = new DataDictionary();
            original.Add("key", new DataToken("value"));

            DataDictionary clone = TsJson.Clone(original);

            Assert.IsNotNull(clone);
            Assert.AreEqual("value", clone["key"].String);
        }

        [Test]
        public void Clone_MutatingClone_DoesNotAffectOriginal()
        {
            var original = new DataDictionary();
            original.Add("key", new DataToken("value"));

            DataDictionary clone = TsJson.Clone(original);
            clone["key"] = new DataToken("mutated");

            Assert.AreEqual("value", original["key"].String);
            Assert.AreEqual("mutated", clone["key"].String);
        }

        [Test]
        public void Clone_NestedDictionary_DeepCopiesNestedValues()
        {
            var inner = new DataDictionary();
            inner.Add("inner_key", new DataToken("inner_value"));
            var original = new DataDictionary();
            original.Add("nested", new DataToken(inner));

            DataDictionary clone = TsJson.Clone(original);
            clone["nested"].DataDictionary["inner_key"] = new DataToken("mutated");

            Assert.AreEqual("inner_value", original["nested"].DataDictionary["inner_key"].String);
        }

        [Test]
        public void Clone_EmptyDictionary_ReturnsEmptyDictionary()
        {
            DataDictionary clone = TsJson.Clone(new DataDictionary());

            Assert.IsNotNull(clone);
            Assert.AreEqual(0, clone.Count);
        }

        [Test]
        public void Clone_NullOriginal_LogsBothSerializeAndDeserializeErrorsAndReturnsNull()
        {
            // Clone(null) = Deserialize(Serialize(null)). Serialize(null) fails and logs,
            // returning "", which Deserialize then also rejects (empty string) and logs —
            // two separate error messages for one Clone(null) call.
            LogAssert.Expect(LogType.Error, "[TsJson] Failed to serialize DataDictionary to JSON.");
            LogAssert.Expect(LogType.Error, "[TsJson] Cannot deserialize null or empty JSON string.");

            DataDictionary clone = TsJson.Clone(null);

            Assert.IsNull(clone);
        }

        [Test]
        public void Clone_NestedList_DeepCopiesListValues()
        {
            var innerList = new DataList();
            innerList.Add(new DataToken("x"));
            var original = new DataDictionary();
            original.Add("list", new DataToken(innerList));

            DataDictionary clone = TsJson.Clone(original);
            clone["list"].DataList[0] = new DataToken("mutated");

            Assert.AreEqual("x", original["list"].DataList[0].String);
        }

    }
}
