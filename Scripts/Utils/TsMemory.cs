using Tsvrc.Core;
using UnityEngine;
using VRC.SDK3.Data;

namespace Tsvrc.Utils
{
    /// <summary>
    /// Local key-value memory store backed by a <see cref="DataDictionary"/>.
    /// Register as a Tsvrc singleton to access via <c>_ts.Memory</c>.
    /// </summary>
    public class TsMemory : TsvrcBehaviour
    {
        private DataDictionary _store = new DataDictionary();

        #region  Write

        /// <summary>Writes <paramref name="value"/> at <paramref name="key"/>, overwriting any existing value.</summary>
        public void Set(string key, DataToken value)
        {
            _store[key] = value;
        }

        /// <summary>
        /// Writes <paramref name="value"/> at <paramref name="key"/> only if the key does not exist yet.
        /// Logs an error if the key is already present. Use <see cref="Set"/> to overwrite intentionally.
        /// </summary>
        public void Add(string key, DataToken value)
        {
            if (_store.ContainsKey(key))
            {
                Debug.LogError($"[TsMemory] Key '{key}' already exists. Use Set to overwrite.");
                return;
            }
            _store[key] = value;
        }

        #endregion

        #region Read

        public bool Has(string key) => _store.ContainsKey(key);

        /// <summary>Returns the raw <see cref="DataToken"/>. Check <c>TokenType</c> before using a typed accessor.</summary>
        public DataToken Get(string key) => _store[key];

        /// <summary>Returns the string value at <paramref name="key"/>.</summary>
        public string GetString(string key) => _store[key].String;

        /// <summary>Returns the value at <paramref name="key"/> as int. Cast-safe after JSON roundtrip.</summary>
        public int GetInt(string key) => (int)_store[key].Double;

        /// <summary>Returns the value at <paramref name="key"/> as float. Cast-safe after JSON roundtrip.</summary>
        public float GetFloat(string key) => (float)_store[key].Double;

        /// <summary>Returns the bool value at <paramref name="key"/>.</summary>
        public bool GetBool(string key) => _store[key].Boolean;

        /// <summary>Returns the nested <see cref="DataDictionary"/> at <paramref name="key"/>.</summary>
        public DataDictionary GetDict(string key) => _store[key].DataDictionary;

        /// <summary>Returns the nested <see cref="DataList"/> at <paramref name="key"/>.</summary>
        public DataList GetList(string key) => _store[key].DataList;

        #endregion

        #region Manage

        /// <summary>Removes the entry at <paramref name="key"/>. No-op if the key does not exist.</summary>
        public void Remove(string key) => _store.Remove(key);

        /// <summary>Removes all entries from the store.</summary>
        public void Clear() => _store.Clear();

        #endregion
    }
}
