#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    // Open via Tsvrc > Tools > Mesh Combiner.
    internal class MeshCombinerWindow : EditorWindow
    {
        private readonly List<MeshFilter> _sources = new List<MeshFilter>();
        private Transform _root;
        private string _savePath = "Assets/CombinedMesh.asset";
        private bool _deactivateSources = true;
        private bool _recalculateNormals = false;
        private bool _excludeEditorOnly = true;
        private Vector2 _scroll;

        // Cached every time the sources list changes to avoid allocating on every repaint.
        private List<MeshFilter> _validCache = new List<MeshFilter>();
        private readonly HashSet<MeshFilter> _seenSet = new HashSet<MeshFilter>();
        private bool _validDirty = true;

        // ValidateSavePath result cached per-frame to avoid redundant string checks.
        private string _cachedPathError;
        private bool _pathErrorDirty = true;

        private string _statusMessage;
        private MessageType _statusType;

        [MenuItem("Tsvrc/Tools/Mesh Combiner")]
        private static void Open()
        {
            var window = GetWindow<MeshCombinerWindow>("Mesh Combiner");
            window.minSize = new Vector2(340, 360);
            window.Show();
        }

        private void OnGUI()
        {
            // Reset the path error cache so it recomputes once per repaint.
            _pathErrorDirty = true;

            EditorGUILayout.LabelField("Mesh Combiner", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Select GameObjects in the scene and click \"Load from Selection\", or add MeshFilter slots manually. " +
                "Then choose a save path and click Combine.",
                MessageType.None);
            EditorGUILayout.Space();

            DrawSources();
            EditorGUILayout.Space();
            DrawSettings();
            EditorGUILayout.Space();
            DrawCombineButton();

            if (!string.IsNullOrEmpty(_statusMessage))
                EditorGUILayout.HelpBox(_statusMessage, _statusType);
        }

        private void DrawSources()
        {
            EditorGUILayout.LabelField("Sources", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Load from Selection",
                "Replaces the current list with all MeshFilters found in the selected scene objects (including children).")))
            {
                if (_sources.Count == 0 || EditorUtility.DisplayDialog(
                        "Replace sources?",
                        "This will replace the current source list with the scene selection. Continue?",
                        "Replace", "Cancel"))
                {
                    LoadFromSelection();
                }
            }
            if (GUILayout.Button(new GUIContent("Clear", "Remove all entries from the list."), GUILayout.Width(52)))
            {
                _sources.Clear();
                InvalidateCache();
                _statusMessage = null;
            }
            EditorGUILayout.EndHorizontal();

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(160));

            for (int i = 0; i < _sources.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                var prev = _sources[i];
                _sources[i] = (MeshFilter)EditorGUILayout.ObjectField(_sources[i], typeof(MeshFilter), true);
                if (_sources[i] != prev) { InvalidateCache(); _statusMessage = null; }
                if (GUILayout.Button("x", GUILayout.Width(22)))
                {
                    _sources.RemoveAt(i);
                    InvalidateCache();
                    _statusMessage = null;
                    i--;
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("+ Add Slot"))
            {
                _sources.Add(null);
                InvalidateCache();
                _statusMessage = null;
            }

            var valid = GetValid();
            EditorGUILayout.LabelField($"{valid.Count} valid source(s) of {_sources.Count} slot(s)", EditorStyles.miniLabel);
        }

        private void DrawSettings()
        {
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);

            _root = (Transform)EditorGUILayout.ObjectField(
                new GUIContent("Root Transform",
                    "The combined mesh is expressed in this transform's local space. " +
                    "The new GameObject is placed as a child of this transform.\n" +
                    "Leave empty to use world space."),
                _root, typeof(Transform), allowSceneObjects: true);

            EditorGUILayout.BeginHorizontal();
            _savePath = EditorGUILayout.TextField(
                new GUIContent("Save Path", "Project-relative path for the mesh asset. Must start with \"Assets/\" and end with \".asset\"."),
                _savePath);
            if (GUILayout.Button("…", GUILayout.Width(28)))
                PickSavePath();
            EditorGUILayout.EndHorizontal();

            var pathError = GetPathError();
            if (pathError != null)
                EditorGUILayout.HelpBox(pathError, MessageType.Error);

            _deactivateSources = EditorGUILayout.Toggle(
                new GUIContent("Deactivate Sources",
                    "Deactivate source GameObjects after combining. Undoable."),
                _deactivateSources);

            _recalculateNormals = EditorGUILayout.Toggle(
                new GUIContent("Recalculate Normals",
                    "Recompute normals from geometry after combining.\n" +
                    "Disable to preserve baked or hand-authored normals from the source meshes."),
                _recalculateNormals);

            _excludeEditorOnly = EditorGUILayout.Toggle(
                new GUIContent("Exclude EditorOnly",
                    "Skip source GameObjects tagged \"EditorOnly\" when combining."),
                _excludeEditorOnly);
        }

        private void DrawCombineButton()
        {
            var valid = GetValid();
            var canCombine = valid.Count > 0 && GetPathError() == null;

            using (new EditorGUI.DisabledScope(!canCombine))
            {
                if (GUILayout.Button("Combine", GUILayout.Height(28)))
                    Execute(valid);
            }

            if (valid.Count == 0)
                EditorGUILayout.HelpBox("Add at least one MeshFilter to combine.", MessageType.Warning);
        }

        private void LoadFromSelection()
        {
            _sources.Clear();
            // Reuse the field-level HashSet to avoid allocating on every call.
            _seenSet.Clear();
            foreach (var go in Selection.gameObjects)
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                    if (_seenSet.Add(mf))
                        _sources.Add(mf);
            InvalidateCache();
            _statusMessage = null;
        }

        private void PickSavePath()
        {
            var path = EditorUtility.SaveFilePanelInProject(
                "Save Combined Mesh", "CombinedMesh", "asset", "Choose save location");
            if (!string.IsNullOrEmpty(path))
                _savePath = path;
        }

        private void Execute(List<MeshFilter> valid)
        {
            Mesh combinedMesh = null;
            try
            {
                var result = MeshCombinerTool.Combine(valid, _root, _recalculateNormals, _excludeEditorOnly);
                combinedMesh = result.Mesh;

                // Overwrite existing asset rather than throwing.
                // Use Object (not Mesh) so any asset type at the path is caught.
                if (AssetDatabase.LoadAssetAtPath<Object>(_savePath) != null)
                    AssetDatabase.DeleteAsset(_savePath);

                AssetDatabase.CreateAsset(combinedMesh, _savePath);
                // Save only this asset, not all dirty assets project-wide.
                AssetDatabase.SaveAssetIfDirty(combinedMesh);

                var go = new GameObject("CombinedMesh");
                Undo.RegisterCreatedObjectUndo(go, "Combine Meshes");

                if (_root != null)
                    go.transform.SetParent(_root, worldPositionStays: false);

                go.AddComponent<MeshFilter>().sharedMesh = combinedMesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = result.Materials;

                if (_deactivateSources)
                {
                    foreach (var mf in valid)
                    {
                        // RecordObject must be called BEFORE the mutation so Undo captures the previous state.
                        Undo.RecordObject(mf.gameObject, "Combine Meshes");
                        mf.gameObject.SetActive(false);
                    }
                }

                Selection.activeGameObject = go;

                _statusMessage = $"Done \u2014 {combinedMesh.vertexCount:N0} vertices, {result.Materials.Length} material(s) saved to {_savePath}";
                _statusType = MessageType.Info;
                Debug.Log($"[Tsvrc] Mesh combined \u2192 {_savePath}  ({result.Materials.Length} material(s), {combinedMesh.vertexCount} vertices)");
            }
            catch (System.Exception ex)
            {
                // Destroy the mesh only if it was never handed off to the AssetDatabase.
                if (combinedMesh != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(combinedMesh)))
                    Object.DestroyImmediate(combinedMesh);

                _statusMessage = $"Combine failed: {ex.Message}";
                _statusType = MessageType.Error;
                Debug.LogException(ex);
            }
        }

        private void InvalidateCache() => _validDirty = true;

        private List<MeshFilter> GetValid()
        {
            if (!_validDirty) return _validCache;
            _validCache.Clear();
            _seenSet.Clear();
            foreach (var mf in _sources)
                if (mf != null && _seenSet.Add(mf))
                    _validCache.Add(mf);
            _validDirty = false;
            return _validCache;
        }

        // Cached per-frame (reset at start of OnGUI) to avoid redundant string ops per repaint.
        private string GetPathError()
        {
            if (!_pathErrorDirty) return _cachedPathError;
            _cachedPathError = ComputePathError();
            _pathErrorDirty = false;
            return _cachedPathError;
        }

        private string ComputePathError()
        {
            if (string.IsNullOrWhiteSpace(_savePath))
                return "Save path is empty.";
            if (!_savePath.StartsWith("Assets/"))
                return "Save path must start with \"Assets/\".";
            if (!_savePath.EndsWith(".asset"))
                return "Save path must end with \".asset\".";
            return null;
        }
    }
}
#endif
