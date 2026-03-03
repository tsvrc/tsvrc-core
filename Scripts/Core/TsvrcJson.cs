using UnityEngine;
using VRC.SDK3.Data;

namespace Tsvrc.Core
{
    public static class TsvrcJson
    {
        /// <summary>Serializes a <see cref="DataDictionary"/> to a minified JSON string.</summary>
        public static string Serialize(DataDictionary dict)
        {
            if (VRCJson.TrySerializeToJson(dict, JsonExportType.Minify, out DataToken result))
                return result.String;

            Debug.LogError("[TsvrcJson] Failed to serialize DataDictionary to JSON.");
            return string.Empty;
        }

        /// <summary>Deserializes a JSON string back to a <see cref="DataDictionary"/>.</summary>
        public static DataDictionary Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                Debug.LogError("[TsvrcJson] Cannot deserialize null or empty JSON string.");
                return null;
            }

            if (VRCJson.TryDeserializeFromJson(json, out DataToken result))
            {
                if (result.TokenType == TokenType.DataDictionary)
                    return result.DataDictionary;

                Debug.LogError("[TsvrcJson] Deserialized JSON is not a DataDictionary.");
                return null;
            }

            Debug.LogError("[TsvrcJson] Failed to deserialize JSON string to DataDictionary.");
            return null;
        }

        /// <summary>Deep-clones a <see cref="DataDictionary"/> via JSON round-trip.</summary>
        public static DataDictionary Clone(DataDictionary original)
        {
            return Deserialize(Serialize(original));
        }
    }
}
