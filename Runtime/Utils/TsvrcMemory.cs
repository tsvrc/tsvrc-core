using Tsvrc.Core;
using UdonSharp;
using VRC.SDK3.Data;
using VRC.SDK3.Persistence;
using VRC.SDKBase;

namespace Tsvrc.Utils
{
    /// <summary>
    /// Unified key-value memory store with three isolated tiers: ephemeral, persistent, and synced.
    /// Tiers are exclusive: a key is either ephemeral, persistent, or synced, never a combination.
    /// Use <see cref="Register"/> before <see cref="Add"/> to declare a key's behavior.
    /// Unregistered keys are ephemeral. Access via <c>_ts.Memory</c>.
    /// </summary>
    /// <remarks>
    /// <b>Synced change notifications:</b> Subscribe to <see cref="OnSyncedChangedEvent"/> via
    /// <c>TsSubscribe(this, TsvrcMemory.OnSyncedChangedEvent, nameof(YourCallback))</c> to be notified
    /// whenever the synced store is updated from the network. Not emitted for local <see cref="Set"/> calls.<br/><br/>
    /// <b>PlayerData size limit:</b> VRChat allows 100 KB of PlayerData per player per world.
    /// VRChat compresses data before storing it, so easily compressible data may exceed 100 KB uncompressed.
    /// If the limit is exceeded, VRChat logs an error and the write is silently dropped, no exception is thrown.
    /// Keep persistent values small and avoid storing large strings, dicts, or lists.<br/><br/>
    /// <b>OnPlayerLeft restriction:</b> VRChat cannot commit PlayerData writes that happen inside
    /// <c>OnPlayerLeft</c>. Avoid calling <see cref="Set"/> on persistent keys inside that event.<br/><br/>
    /// <b>World scope:</b> Persistent data is scoped to this world and cannot be shared between different worlds.<br/><br/>
    /// <b>Reference:</b> <see href="https://creators.vrchat.com/worlds/udon/persistence/">VRChat Persistence docs</see>
    /// </remarks>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    [TsWorldExtensionPoint("TsMemory")]
    public class TsvrcMemory : TsvrcBehaviour
    {
        protected override bool IsTsvrcInternal => true;

        // Flag values. Stored as double in _registry to survive DataToken roundtrip.
        private const int FLAG_PERSIST = 1;
        private const int FLAG_SYNCED = 2;

        // Type codes. Stored as double in _types for the same reason.
        private const int TYPE_STRING = 1;
        private const int TYPE_BOOL = 2;
        private const int TYPE_FLOAT = 3;
        private const int TYPE_INT = 4;
        private const int TYPE_DOUBLE = 5;
        private const int TYPE_DICT = 6;
        private const int TYPE_LIST = 7;

        /// <summary>Ephemeral store. Local only, cleared on world exit.</summary>
        private DataDictionary _store = new DataDictionary();

        /// <summary>Persistent store. Local cache backed by PlayerData.</summary>
        private DataDictionary _persistStore = new DataDictionary();

        /// <summary>
        /// Synced store. Replicated to all players.
        /// DataDictionary is not a supported UdonSynced type, so it is serialized to <see cref="_syncedJson"/>.
        /// </summary>
        private DataDictionary _syncedStore = new DataDictionary();
        [UdonSynced] private string _syncedJson = "{}";

        /// <summary>Maps key to flag as double (1=persist, 2=synced). Only registered keys are present.</summary>
        private DataDictionary _registry = new DataDictionary();

        /// <summary>Maps key to type code as double. Populated for all persist-registered keys.</summary>
        private DataDictionary _types = new DataDictionary();

        private bool _playerRestored;

        /// <summary>True after the local player's saved data has been loaded via <c>OnPlayerRestored</c>.</summary>
        public bool IsPlayerRestored => _playerRestored;

        /// <summary>
        /// Declares the storage tier for <paramref name="key"/>. Must be called before <see cref="Add"/>.
        /// Tiers are exclusive: a key is either persistent or synced, not both.
        /// Unregistered keys are ephemeral. Logs an error if the key is already registered.
        /// </summary>
        /// <param name="persist">Value survives sessions via PlayerData (local player only).</param>
        /// <param name="synced">Value is replicated to all players. Mutually exclusive with persist.</param>
        public void Register(string key, bool persist, bool synced)
        {
            if (_registry.ContainsKey(key))
            {
                LogError($"Key '{key}' is already registered.");
                return;
            }

            if (persist && synced)
            {
                LogError($"Key '{key}': persist and synced are mutually exclusive.");
                return;
            }

            if (!persist && !synced)
            {
                LogError($"Key '{key}': Register called with no tier. Unregistered keys are already ephemeral.");
                return;
            }

            int flags = synced ? FLAG_SYNCED : FLAG_PERSIST;
            _registry[key] = new DataToken((double)flags);
        }

        /// <summary>
        /// Writes <paramref name="value"/> at <paramref name="key"/>, overwriting any existing value.
        /// For synced keys, ownership is transferred to the local player if not already owner.
        /// </summary>
        public void Set(string key, DataToken value)
        {
            int flags = _Flags(key);

            if (_IsSynced(flags) && !Networking.IsOwner(gameObject))
                Networking.SetOwner(Networking.LocalPlayer, gameObject);

            _Store(flags)[key] = value;
            _UpdateType(key, flags, value);
            if (_IsPersist(flags)) _WriteToPd(key, value);
            if (_IsSynced(flags)) _Serialize();
        }

        /// <summary>
        /// Sets the default for <paramref name="key"/> if it has no value yet.
        /// Logs an error if the key already has a value. Use <see cref="Set"/> to overwrite.
        /// For synced keys, silently skips if the network has already populated the key.
        /// Does not write to PlayerData. <c>OnPlayerRestored</c> will override with saved data.
        /// </summary>
        public void Add(string key, DataToken value)
        {
            int flags = _Flags(key);
            DataDictionary store = _Store(flags);
            if (store.ContainsKey(key))
            {
                if (!_IsSynced(flags))
                    LogError($"Key '{key}' already exists. Use Set to overwrite.");
                return;
            }
            store[key] = value;
            _UpdateType(key, flags, value);
        }

        /// <summary>Returns true if the key has a value in its store.</summary>
        public bool Has(string key) => _Store(_Flags(key)).ContainsKey(key);

        /// <summary>Returns the raw <see cref="DataToken"/>.</summary>
        public DataToken Get(string key) => _Store(_Flags(key))[key];

        /// <summary>Returns the string value at <paramref name="key"/>.</summary>
        public string GetString(string key) => _Store(_Flags(key))[key].String;

        /// <summary>Returns the value at <paramref name="key"/> as int.</summary>
        public int GetInt(string key)
        {
            DataToken t = _Store(_Flags(key))[key];
            return t.TokenType == TokenType.Int ? t.Int : (int)t.Double;
        }

        /// <summary>Returns the value at <paramref name="key"/> as float.</summary>
        public float GetFloat(string key)
        {
            DataToken t = _Store(_Flags(key))[key];
            return t.TokenType == TokenType.Float ? t.Float : (float)t.Double;
        }

        /// <summary>Returns the bool value at <paramref name="key"/>.</summary>
        public bool GetBool(string key) => _Store(_Flags(key))[key].Boolean;

        /// <summary>Returns the nested <see cref="DataDictionary"/> at <paramref name="key"/>.</summary>
        public DataDictionary GetDict(string key) => _Store(_Flags(key))[key].DataDictionary;

        /// <summary>Returns the nested <see cref="DataList"/> at <paramref name="key"/>.</summary>
        public DataList GetList(string key) => _Store(_Flags(key))[key].DataList;

        /// <summary>
        /// Removes <paramref name="key"/> from its store.
        /// The key's registration is preserved, so <see cref="Add"/> can be called again without re-registering.
        /// For synced keys, ownership is transferred to the local player if not already owner.
        /// Note: PlayerData keys cannot be deleted. Only the local persist cache is cleared.
        /// </summary>
        public void Remove(string key)
        {
            int flags = _Flags(key);

            if (_IsSynced(flags))
            {
                if (!_syncedStore.ContainsKey(key)) return;
                if (!Networking.IsOwner(gameObject))
                    Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }

            if (_IsPersist(flags) && _persistStore.ContainsKey(key))
                LogWarning($"'{key}' is persistent. PlayerData cannot be deleted; local cache cleared.");

            _Store(flags).Remove(key);
            _types.Remove(key);
            if (_IsSynced(flags)) _Serialize();
        }

        /// <summary>
        /// Clears all values from all stores. PlayerData is not affected.
        /// Key registrations in <see cref="Register"/> are preserved after clear.
        /// </summary>
        public void Clear()
        {
            _store.Clear();
            _persistStore.Clear();
            _types.Clear();

            if (_syncedStore.Count == 0) return;

            if (!Networking.IsOwner(gameObject))
                Networking.SetOwner(Networking.LocalPlayer, gameObject);

            _syncedStore.Clear();
            _Serialize();
        }

        /// <summary>Event name for synced store change notifications. See class remarks for usage.</summary>
        public const string OnSyncedChangedEvent = "OnSyncedChanged";

        /// <summary>
        /// Called by VRChat when synced data arrives from the network owner.
        /// Deserializes <see cref="_syncedJson"/> into the synced store and emits
        /// <see cref="OnSyncedChangedEvent"/> to all subscribers.
        /// </summary>
        public override void OnDeserialization()
        {
            DataToken result = TsJson.DeserializeToken(_syncedJson);
            if (result.TokenType != TokenType.DataDictionary) return;
            _syncedStore = result.DataDictionary;
            TsEmit(OnSyncedChangedEvent);
        }

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (!player.isLocal) return;
            _playerRestored = true;

            DataList keys = _registry.GetKeys();
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i].String;
                int flags = (int)_registry[key].Double;
                if (!_IsPersist(flags) || !PlayerData.HasKey(player, key)) continue;

                if (!_types.ContainsKey(key))
                {
                    LogWarning($"Cannot restore '{key}' from PlayerData: type unknown. Call Add or Set before OnPlayerRestored.");
                    continue;
                }

                _persistStore[key] = _LoadFromPd(player, key);
            }
        }

        private int _Flags(string key) =>
            _registry.ContainsKey(key) ? (int)_registry[key].Double : 0;

        private DataDictionary _Store(int flags) =>
            _IsPersist(flags) ? _persistStore :
            _IsSynced(flags) ? _syncedStore :
            _store;

        private bool _IsPersist(int flags) => flags == FLAG_PERSIST;
        private bool _IsSynced(int flags) => flags == FLAG_SYNCED;

        private void _UpdateType(string key, int flags, DataToken value)
        {
            if (_IsPersist(flags))
                _types[key] = new DataToken((double)_TypeCode(value));
        }

        private void _Serialize()
        {
            _syncedJson = TsJson.Serialize(_syncedStore);
            RequestSerialization();
        }

        private int _TypeCode(DataToken value)
        {
            if (value.TokenType == TokenType.String) return TYPE_STRING;
            if (value.TokenType == TokenType.Boolean) return TYPE_BOOL;
            if (value.TokenType == TokenType.Float) return TYPE_FLOAT;
            if (value.TokenType == TokenType.Int) return TYPE_INT;
            if (value.TokenType == TokenType.DataDictionary) return TYPE_DICT;
            if (value.TokenType == TokenType.DataList) return TYPE_LIST;
            return TYPE_DOUBLE;
        }

        private void _WriteToPd(string key, DataToken value)
        {
            if (!_playerRestored)
            {
                LogWarning($"Set('{key}') before OnPlayerRestored. Value will not be saved to PlayerData.");
                return;
            }
            switch ((int)_types[key].Double)
            {
                case TYPE_STRING: PlayerData.SetString(key, value.String); break;
                case TYPE_BOOL: PlayerData.SetBool(key, value.Boolean); break;
                case TYPE_FLOAT: PlayerData.SetFloat(key, value.Float); break;
                case TYPE_INT: PlayerData.SetInt(key, value.Int); break;
                case TYPE_DICT:
                case TYPE_LIST: PlayerData.SetString(key, TsJson.SerializeToken(value)); break;
                default: PlayerData.SetDouble(key, value.Double); break;
            }
        }

        private DataToken _LoadFromPd(VRCPlayerApi player, string key)
        {
            switch ((int)_types[key].Double)
            {
                case TYPE_STRING: return new DataToken(PlayerData.GetString(player, key));
                case TYPE_BOOL: return new DataToken(PlayerData.GetBool(player, key));
                case TYPE_FLOAT: return new DataToken(PlayerData.GetFloat(player, key));
                case TYPE_INT: return new DataToken(PlayerData.GetInt(player, key));
                case TYPE_DICT:
                case TYPE_LIST: return TsJson.DeserializeToken(PlayerData.GetString(player, key));
                default: return new DataToken(PlayerData.GetDouble(player, key));
            }
        }
    }
}
