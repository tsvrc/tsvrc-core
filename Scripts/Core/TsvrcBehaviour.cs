using UdonSharp;

namespace Tsvrc.Core
{
    public class TsvrcBehaviour : UdonSharpBehaviour
    {
        protected TsvrcSingleton _singleton;
        protected TsvrcInstance _instance;

        public void ConstructBehaviour(TsvrcSingleton singleton, TsvrcInstance instance)
        {
            _instance = instance;
            _singleton = singleton;
        }
    }
}