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

        // ---- Write ----

        /// <summary>Overwrites the value at <paramref name="key"/> unconditionally.</summary>
        public void Set(string key, DataToken value)
        {
            _store[key] = value;
        }

        public void SetString(string key, string value)
        {
            _store[key] = new DataToken(value);
        }

        public void SetInt(string key, int value)
        {
            _store[key] = new DataToken(value);
        }

        public void SetFloat(string key, float value)
        {
            _store[key] = new DataToken(value);
        }

        public void SetBool(string key, bool value)
        {
            _store[key] = new DataToken(value);
        }

        public void SetDict(string key, DataDictionary value)
        {
            _store[key] = new DataToken(value);
        }

        // ---- Register (write-once) ----

        /// <summary>
        /// Registers a new key. Logs an error if the key already exists — use <see cref="Set"/> to overwrite intentionally.
        /// </summary>
        public void Register(string key, DataToken value)
        {
            if (_store.ContainsKey(key))
            {
                Debug.LogError($"[TsMemory] Key '{key}' is already registered. Use Set to overwrite.");
                return;
            }
            _store[key] = value;
        }

        public void RegisterDict(string key, DataDictionary value)
        {
            Register(key, new DataToken(value));
        }

        // ---- Read ----

        public bool Has(string key)
        {
            return _store.ContainsKey(key);
        }

        /// <summary>Returns the raw <see cref="DataToken"/>. Check <c>TokenType</c> before accessing a typed property.</summary>
        public DataToken Get(string key)
        {
            return _store[key];
        }

        public string GetString(string key, string defaultVal = "")
        {
            return Has(key) ? _store[key].String : defaultVal;
        }

        /// <summary>Reads the value as int. Safe after JSON roundtrip (stored doubles are cast).</summary>
        public int GetInt(string key, int defaultVal = 0)
        {
            return Has(key) ? (int)_store[key].Double : defaultVal;
        }

        /// <summary>Reads the value as float. Safe after JSON roundtrip (stored doubles are cast).</summary>
        public float GetFloat(string key, float defaultVal = 0f)
        {
            return Has(key) ? (float)_store[key].Double : defaultVal;
        }

        public bool GetBool(string key, bool defaultVal = false)
        {
            return Has(key) ? _store[key].Boolean : defaultVal;
        }

        /// <summary>Returns the nested <see cref="DataDictionary"/>, or <c>null</c> if the key is absent.</summary>
        public DataDictionary GetDict(string key)
        {
            return Has(key) ? _store[key].DataDictionary : null;
        }

        // ---- Manage ----

        public void Remove(string key)
        {
            _store.Remove(key);
        }

        public void Clear()
        {
            _store.Clear();
        }

    }
}
