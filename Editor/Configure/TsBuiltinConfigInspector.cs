#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Unlike TsConfig/TsRoot, this package-shipped, every-world-shares-it asset had no custom
    // inspector: a bare default Inspector gives no signal that dragging a project-specific prefab
    // in here (an easy mistake, since it sits next to the package's own real config with a
    // near-identical shape) makes that registration a library-wide "builtin" instead of a
    // per-world one, shared with every teammate on the next pull. Mirrors TsConfigInspector's
    // own banner pattern.
    //
    // Globals/Factories are drawn via the same TsGroupTreeGUI widget Tsvrc > Configure uses for
    // the per-world TsConfig, as two separate trees rather than merged with the world's own
    // config - builtin and per-world registrations are conceptually distinct, so browsing or
    // searching them together would blur that distinction.
    [CustomEditor(typeof(TsBuiltinConfig))]
    internal class TsBuiltinConfigInspector : UnityEditor.Editor
    {
        private readonly TsGroupTreeGUI.State _globalTreeState = new TsGroupTreeGUI.State();
        private readonly TsGroupTreeGUI.State _factoryTreeState = new TsGroupTreeGUI.State();
        private readonly TsGroupTreeGUI.State _poolTreeState = new TsGroupTreeGUI.State();

        // The live group fields are drawn explicitly above via TsGroupTreeGUI instead of
        // DrawDefaultInspector's raw array view, so they're excluded here to avoid showing the
        // same data twice.
        private static readonly string[] ManuallyDrawnProperties =
        {
            "m_Script",
            "GlobalEntries", "GlobalGroups", "GlobalNextGroupId",
            "FactoryEntries", "FactoryGroups", "FactoryNextGroupId",
            "PoolEntries", "PoolGroups", "PoolNextGroupId",
        };

        public override void OnInspectorGUI()
        {
            TsEditorGUI.DrawStatusBox(
                "This is Tsvrc's own package-level builtin config, shared by every world using this " +
                "copy of Tsvrc - anything registered here becomes available to every world, not just " +
                "this one. Register your own world's globals/pool prefabs/factories via " +
                "Tsvrc > Configure instead, unless you specifically intend to add a library-wide builtin.",
                MessageType.Info);

            serializedObject.Update();

            EditorGUILayout.LabelField("Globals", EditorStyles.boldLabel);
            TsGroupTreeGUI.Draw(serializedObject, "GlobalGroups", "GlobalEntries", _globalTreeState,
                "No builtin globals registered yet.", warnDuplicates: true, memberPrefix: "_ts.");

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Factories", EditorStyles.boldLabel);
            TsGroupTreeGUI.Draw(serializedObject, "FactoryGroups", "FactoryEntries", _factoryTreeState,
                "No builtin factory prefabs registered yet.", assetsOnly: true,
                memberPrefix: "Create", memberSuffix: "(parent)", prefixRespectsToggle: false);

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Pool", EditorStyles.boldLabel);
            TsGroupTreeGUI.Draw(serializedObject, "PoolGroups", "PoolEntries", _poolTreeState,
                "No builtin pool prefabs registered yet.", assetsOnly: true);

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(10);
            DrawPropertiesExcluding(serializedObject, ManuallyDrawnProperties);
            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
