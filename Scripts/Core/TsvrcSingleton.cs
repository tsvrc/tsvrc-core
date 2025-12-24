using UdonSharp;
using VRC.SDK3.Data;

namespace Tsvrc.Core
{
    public class TsvrcSingleton : UdonSharpBehaviour
    {
        private DataList _behaviours;

        public void TsConstruct(TsvrcInstance instance)
        {
            OnBeforeInitialize();

            for (int i = 0; i < _behaviours.Count; i++)
            {
                ((TsvrcBehaviour)_behaviours[i].Reference).TsConstruct(this, instance);
            }

            OnAfterInitialize();
        }

        /// <summary>
        /// Adds a TsvrcBehaviour that will act as a singleton.
        /// These behaviours must be assigned before initialization.
        /// </summary>
        protected TsvrcBehaviour AddSingleton(TsvrcBehaviour behaviour)
        {
            if (_behaviours == null)
                _behaviours = new DataList();

            _behaviours.Add(behaviour);
            return behaviour;
        }

        /// <summary>
        /// Called before initializing Tsvrc behaviours. Override this method to initialize
        /// non-Tsvrc behaviours before the Tsvrc ones.
        /// </summary>
        protected virtual void OnBeforeInitialize() { }

        /// <summary>
        /// Called after initializing Tsvrc behaviours. Override this method to initialize
        /// non-Tsvrc behaviours after the Tsvrc ones.
        /// </summary>
        protected virtual void OnAfterInitialize() { }
    }
}