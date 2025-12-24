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
        }
    }
}