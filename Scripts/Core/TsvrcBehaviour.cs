using UdonSharp;

namespace Tsvrc.Core
{
    public class TsvrcBehaviour : UdonSharpBehaviour
    {
        public TsvrcSingleton Singleton { get; private set; }
        public TsvrcInstance Instance { get; private set; }

        public void TsConstruct(TsvrcSingleton singleton, TsvrcInstance instance)
        {
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