using UnityEngine;
using VRC.SDK3.Data;

namespace Tsvrc.Utils
{
    /// <summary>
    /// Static helpers for serializing and deserializing VRChat DataDictionary and DataToken
    /// objects to and from JSON using VRCJson.
    /// </summary>
    public static class TsJson
    {
        /// <summary>Serializes a <see cref="DataDictionary"/> to a minified JSON string.</summary>
        /// <returns>The JSON string, or an empty string if serialization fails.</returns>
        public static string Serialize(DataDictionary dict)
        {
            if (VRCJson.TrySerializeToJson(dict, JsonExportType.Minify, out DataToken result))
                return result.String;

            Debug.LogError("[TsJson] Failed to serialize DataDictionary to JSON.");
            return string.Empty;
        }

        /// <summary>Deserializes a JSON string to a <see cref="DataDictionary"/>.</summary>
        /// <returns>The deserialized dictionary, or null if the input is empty or the JSON is not a DataDictionary.</returns>
        public static DataDictionary Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                Debug.LogError("[TsJson] Cannot deserialize null or empty JSON string.");
                return null;
            }

            if (VRCJson.TryDeserializeFromJson(json, out DataToken result))
            {
                if (result.TokenType == TokenType.DataDictionary)
                    return result.DataDictionary;

                Debug.LogError("[TsJson] Deserialized JSON is not a DataDictionary.");
                return null;
            }

            Debug.LogError("[TsJson] Failed to deserialize JSON string to DataDictionary.");
            return null;
        }

        /// <summary>Serializes a <see cref="DataToken"/> (dictionary or list) to a minified JSON string.</summary>
        /// <returns>The JSON string, or an empty string if serialization fails.</returns>
        public static string SerializeToken(DataToken token)
        {
            if (VRCJson.TrySerializeToJson(token, JsonExportType.Minify, out DataToken result))
                return result.String;

            Debug.LogError("[TsJson] Failed to serialize DataToken to JSON.");
            return string.Empty;
        }

        /// <summary>Deserializes a JSON string to a raw <see cref="DataToken"/>.</summary>
        /// <returns>The deserialized token, or the default DataToken value if the input is empty or invalid.</returns>
        public static DataToken DeserializeToken(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                Debug.LogError("[TsJson] Cannot deserialize null or empty JSON string.");
                return default;
            }

            if (VRCJson.TryDeserializeFromJson(json, out DataToken result))
                return result;

            Debug.LogError("[TsJson] Failed to deserialize JSON string.");
            return default;
        }

        /// <summary>Deep-clones a <see cref="DataDictionary"/> via a JSON round-trip.</summary>
        /// <returns>A new DataDictionary with the same contents, or null if the original cannot be serialized.</returns>
        public static DataDictionary Clone(DataDictionary original)
        {
            return Deserialize(Serialize(original));
        }
    }
}
