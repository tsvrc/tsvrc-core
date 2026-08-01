#if UNITY_EDITOR
using UnityEditor;

namespace Tsvrc.Editor
{
    // Unlike TsConfig/TsRoot, this package-shipped, every-world-shares-it asset had no custom
    // inspector: a bare default Inspector gives no signal that dragging a project-specific prefab
    // in here (an easy mistake, since it sits next to the package's own real config with a
    // near-identical shape) makes that registration a library-wide "builtin" instead of a
    // per-world one, shared with every teammate on the next pull. Mirrors TsConfigInspector's
    // own banner pattern.
    [CustomEditor(typeof(TsBuiltinConfig))]
    internal class TsBuiltinConfigInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            TsEditorGUI.DrawStatusBox(
                "This is Tsvrc's own package-level builtin config, shared by every world using this " +
                "copy of Tsvrc - anything registered here becomes available to every world, not just " +
                "this one. Register your own world's singletons/pool prefabs/factories via " +
                "Tsvrc > Configure instead, unless you specifically intend to add a library-wide builtin.",
                MessageType.Info);
            DrawDefaultInspector();
        }
    }
}
#endif
