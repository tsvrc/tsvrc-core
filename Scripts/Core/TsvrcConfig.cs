using UdonSharp;
using UnityEngine;

namespace Tsvrc.Core
{
    // EditorOnly — scanned by TsvrcCompiler, stripped from VRChat build.
    public class TsvrcConfig : UdonSharpBehaviour
    {
        public TsvrcInstance Instance;
        [Tooltip("Singleton behaviours that will be initialized by Tsvrc. Assign here any object that you want to be a singleton, and assign the TsvrcBehaviour scripts that they should use as a singleton class.")]
        public Object[] Singletons;
        [Tooltip("Instance behaviours that will be initialized by Tsvrc. Assign here any object that you want to be an instance, and assign the TsvrcBehaviour scripts that they should use as an instance class.")]
        public TsvrcBehaviour[] Behaviours;

        [Tooltip("Internal behaviours that will be initialized by Tsvrc. These are used internally by Tsvrc and should not be assigned manually.")]
        public TsvrcBehaviour[] InternalBehaviours;
    }
}