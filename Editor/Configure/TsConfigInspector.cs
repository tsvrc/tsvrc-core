#if UNITY_EDITOR
using Tsvrc.Config;
using UnityEditor;

namespace Tsvrc.Editor
{
    // TsConfig is tagged EditorOnly, so a user can stumble onto it directly in the Hierarchy
    // without knowing Tsvrc > Configure exists - the banner below points them there. No raw
    // field editing is offered here: Tsvrc > Configure's Apply/Discard batching (see
    // TsPendingConfigEdit) is the only place these fields may be mutated, so a bypass here
    // would defeat that guarantee entirely.
    [CustomEditor(typeof(TsConfig))]
    internal class TsConfigInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            TsEditorGUI.DrawManagedByConfigureBanner(
                "This object is auto-created and self-healed by Tsvrc. Its fields are only editable " +
                "through Tsvrc > Configure, which gives the same data with per-tab guidance, " +
                "empty-state hints, and explicit Apply/Discard.");
        }
    }
}
#endif
