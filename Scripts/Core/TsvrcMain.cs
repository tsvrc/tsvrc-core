using UdonSharp;
using UnityEngine;

namespace Tsvrc.Core
{
    public class TsvrcMain : UdonSharpBehaviour
    {
        protected void Main(TsvrcSingleton singleton, TsvrcInstance instance)
        {
            if (singleton == null)
            {
                Debug.LogError("Singleton is not assigned in TsvrcMain.");
            }
            singleton.Initialize(instance);
            singleton.ConstructSingleton();

            Debug.Log("TsvrcMain started");
        }
    }
}