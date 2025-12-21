using UdonSharp;
using UnityEngine;

namespace Tsvrc.Core
{
    public class TsvrcSingleton : UdonSharpBehaviour
    {
        private TsvrcInstance _instance;
        private TsvrcBehaviour[] _behaviours = null;

        public void Initialize(TsvrcInstance instance)
        {
            _instance = instance;
        }

        public virtual void ConstructSingleton()
        {
            if (_behaviours == null)
            {
                Debug.LogWarning("No behaviours registered in TsvrcSingleton.");
            }
            else
            {
                int length = _behaviours.Length;
                for (int i = 0; i < length; i++)
                {
                    if (_behaviours[i] != null)
                    {
                        _behaviours[i].ConstructBehaviour(this, _instance);
                    }
                    else
                    {
                        Debug.LogError($"[TsvrcSingleton] Behaviour at index {i} is null!");
                    }
                }
            }
        }

        public void RegisterBehaviour(TsvrcBehaviour behaviour)
        {
            if (_behaviours == null)
            {
                _behaviours = new TsvrcBehaviour[0];
            }

            int length = _behaviours.Length;
            TsvrcBehaviour[] newBehaviours = new TsvrcBehaviour[length + 1];
            for (int i = 0; i < length; i++)
            {
                newBehaviours[i] = _behaviours[i];
            }
            newBehaviours[length] = behaviour;
            _behaviours = newBehaviours;
        }
    }
}