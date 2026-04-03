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
        [Tooltip("TsvrcBehaviours placed in the scene at compile time as pool slots. Because they exist before play, VRChat assigns them stable network IDs so they can receive network events, unlike runtime-instantiated objects. Each entry generates a Get<TypeName>() method on CompiledTsvrc. Example: add BulletBehaviour here, then call _ts.GetBulletBehaviour() at runtime.")]
        public TsvrcBehaviour[] TsvrcBehaviourPool;
        [Tooltip("TsvrcBehaviours that are always active in the scene, not pooled. TsConstruct(_ts) is called on each at startup, giving them access to _ts. Example: add your CollisionHandler here to have it initialized when the world loads.")]
        public TsvrcBehaviour[] TsvrcBehaviourConstruct;
    }
}