using UnityEngine;

namespace Tsvrc.Core
{
    // EditorOnly: tag this GameObject in the scene as "EditorOnly". Unity strips it from the build entirely.
    // Configure via Tsvrc > Configure. Do not modify at runtime.
    [AddComponentMenu("Tsvrc/Tsvrc Config")]
    public class TsvrcConfig : MonoBehaviour
    {
        [Tooltip("The single TsvrcInstance for this world. If set, TsConstruct(_ts) and OnInstanceStart() are called on it after all other constructs.")]
        public TsvrcInstance Instance;

        [Tooltip("Scene objects exposed as named fields on CompiledTsvrc. Drop a GameObject or Component here; each entry becomes a typed _ts.FieldName accessor available to all TsvrcBehaviours. Example: drag your AudioManager here to access it as _ts.AudioManager.")]
        public Object[] Singletons;
        [Tooltip("Any pooled objects placed in the scene at compile time. Components that inherit from TsvrcBehaviour will have TsConstruct called at scene start. Other types are simply wired as references. Each entry generates a Get<TypeName>() method on CompiledTsvrc. Use [WirePool] attribute in code to declare your intent. Mark each pooled type only once.")]
        public Object[] PooledObjects;
        [Tooltip("TsvrcBehaviours that are always active in the scene, not pooled. TsConstruct(_ts) is called on each at startup, giving them access to _ts. Example: add your CollisionHandler here to have it initialized when the world loads.")]
        public TsvrcBehaviour[] TsvrcBehaviourConstruct;
        [Tooltip("Factory groups for runtime-instantiated (non-networked) prefabs. Each group generates Create{GroupName}{PrefabName}(Transform parent) methods on CompiledTsvrc.")]
        public TsvrcFactoryGroup[] Factories;
    }
}