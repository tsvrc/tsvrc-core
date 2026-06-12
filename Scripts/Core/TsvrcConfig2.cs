using UnityEngine;

namespace Tsvrc.Core
{
    // V2 compiler config. Stored as a ScriptableObject at Assets/.tsvrc/TsvrcConfig.asset.
    // Created automatically on first compile. Edit via Tsvrc > Configure (v2).
    public class TsvrcConfig2 : ScriptableObject
    {
        public TsvrcInstance Instance;
        public Object[] Singletons;
        public Object[] PooledObjects;
        public TsvrcBehaviour[] Constructs;
        public TsvrcFactoryGroup[] Factories;
    }
}
