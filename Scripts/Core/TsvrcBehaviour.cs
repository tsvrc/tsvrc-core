using Tsvrc.Core.Compiled;
using UdonSharp;
using UnityEngine;

namespace Tsvrc.Core
{
    public class TsvrcBehaviour : UdonSharpBehaviour
    {
        public TsvrcSingleton Singleton { get; private set; }
        public TsvrcInstance Instance { get; private set; }

        protected CompiledTsvrc _ts;

        private bool _isCreated = false;

        public void TsConstruct(CompiledTsvrc context)
        {
            if (_isCreated)
            {
                Debug.LogWarning($"[TsvrcBehaviour] {name} is already constructed. Ignoring duplicate construction.");
                return;
            }

            _isCreated = true;
            _ts = context;
            TsStart();
        }

        public void TsConstruct(TsvrcSingleton singleton, TsvrcInstance instance)
        {
            _isCreated = true;

            Instance = instance;
            Singleton = singleton;

            TsStart();
        }

        /// <summary>
        /// Constructs this behavious from another behaviour.
        /// </summary>
        public void TsConstruct(TsvrcBehaviour behaviour)
        {
            TsConstruct(behaviour.Singleton, behaviour.Instance);
        }

        #region Virtual Methods

        protected virtual void TsStart() { }

        #endregion
    }
}