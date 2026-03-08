using Tsvrc.Core;
using UdonSharp;
using VRC.SDK3.Data;

namespace Tsvrc.Network
{
    public enum TsvrcNetworkEvent
    {
        OnPlayerJoined = 0,
        OnPlayerLeft = 1
    }

    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class TsvrcNetwork : TsvrcBehaviour
    {
        // _listeners[instanceId]["ref"]      = behaviour
        // _listeners[instanceId][eventKey]   = DataList<methodName>
        private DataDictionary _listeners;

        protected override void TsStart()
        {
            _listeners = new DataDictionary();
        }

        public void AddListener(TsvrcBehaviour behaviour, TsvrcNetworkEvent eventName, string methodName)
        {
            int instanceId = behaviour.gameObject.GetInstanceID();
            int eventKey = (int)eventName;

            if (!_listeners.ContainsKey(instanceId))
            {
                var entry = new DataDictionary();
                entry["ref"] = behaviour;
                _listeners[instanceId] = entry;
            }

            var instanceDict = _listeners[instanceId].DataDictionary;
            instanceDict[eventKey] = methodName;
        }

        public void FireEvent(TsvrcNetworkEvent eventName)
        {
            int eventKey = (int)eventName;
            var keys = _listeners.GetKeys();

            for (int i = 0; i < keys.Count; i++)
            {
                var instanceDict = _listeners[keys[i]].DataDictionary;
                if (!instanceDict.ContainsKey(eventKey)) continue;

                var behaviour = (TsvrcBehaviour)instanceDict["ref"].Reference;
                behaviour.SendCustomEvent(instanceDict[eventKey].String);
            }
        }

        #region UdonSharp Callbacks

        #endregion
    }
}