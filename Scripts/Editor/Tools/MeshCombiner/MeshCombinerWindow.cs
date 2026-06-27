#if UNITY_EDITOR
using System.Collections.Generic;
using UdonSharp;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor
{
    /// <summary>Editor window that combines selected MeshFilter sources into a single multi-material mesh asset. Open via Tsvrc > Tools > Mesh Combiner.</summary>
    internal class MeshCombinerWindow : EditorWindow
    {
        private readonly List<MeshFilter> _sources = new List<MeshFilter>();
        private Transform _root;
        private string _savePath = "Assets/CombinedMesh.asset";
        private bool _deactivateSources = true;
        private bool _recalculateNormals = false;
        private bool _excludeEditorOnly = true;
        private bool _combineColliders = true;
        private Vector2 _scroll;

        // Cached every time the sources list changes to avoid allocating on every repaint.
        private List<MeshFilter> _validCache = new List<MeshFilter>();
        private readonly HashSet<MeshFilter> _seenSet = new HashSet<MeshFilter>();
        private bool _validDirty = true;

        // GameObjects that have colliders but no MeshFilter, collected by LoadFromSelection.
        // They contribute colliders only and are not shown as editable slots in the UI.
        private readonly List<GameObject> _colliderOnlySources = new List<GameObject>();
        private readonly HashSet<GameObject> _seenColliderSet = new HashSet<GameObject>();

        // GameObjects that have a ParticleSystem but no MeshFilter, collected by LoadFromSelection.
        // They are reparented as children of the combined output to preserve their settings.
        private bool _combineParticleSystems = true;
        private readonly List<ParticleSystem> _particleSystemSources = new List<ParticleSystem>();
        private readonly HashSet<GameObject> _seenParticleSet = new HashSet<GameObject>();

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
                "Replaces the current list with all MeshFilters found in the selected scene objects (including children).\n" +
                "Also collects GameObjects that have colliders but no MeshFilter.")))
            {
                if (_sources.Count == 0 && _colliderOnlySources.Count == 0 || EditorUtility.DisplayDialog(
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
                _colliderOnlySources.Clear();
                _seenColliderSet.Clear();
                _particleSystemSources.Clear();
                _seenParticleSet.Clear();
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
            var colliderOnlyText = _colliderOnlySources.Count > 0 ? $" + {_colliderOnlySources.Count} collider-only" : "";
            var psText = _particleSystemSources.Count > 0 ? $" + {_particleSystemSources.Count} particle system(s)" : "";
            EditorGUILayout.LabelField($"{valid.Count} valid source(s){colliderOnlyText}{psText} of {_sources.Count} slot(s)", EditorStyles.miniLabel);
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
                    "Deactivate source GameObjects after combining. Undoable.\n" +
                    "GameObjects with only colliders (no MeshFilter) are also deactivated, but only when Combine Colliders is enabled.\n" +
                    "Sources with an UdonSharpBehaviour are not deactivated. On MeshFilter sources their MeshRenderer is disabled instead " +
                    "so the Udon script and its collider remain active."),
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

            _combineColliders = EditorGUILayout.Toggle(
                new GUIContent("Combine Colliders",
                    "Merge solid MeshColliders into a single collision mesh asset (saved alongside the visual mesh). " +
                    "Box, Sphere, Capsule, and trigger MeshColliders are recreated as child GameObjects under the combined object. " +
                    "GameObjects with colliders but no MeshFilter are included automatically when loading from selection. " +
                    "Disabled colliders and colliders on objects with UdonSharpBehaviour are excluded."),
                _combineColliders);

            _combineParticleSystems = EditorGUILayout.Toggle(
                new GUIContent("Reparent Particle Systems",
                    "Move GameObjects with a ParticleSystem (and no MeshFilter) to be children of the combined output. " +
                    "Particle systems are not merged — they are reparented to preserve their settings and simulation state. " +
                    "GameObjects with UdonSharpBehaviour are excluded."),
                _combineParticleSystems);
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
            _seenSet.Clear();
            _colliderOnlySources.Clear();
            _seenColliderSet.Clear();

            // First pass: collect all GameObjects that have a MeshFilter so we can exclude them
            // from the collider-only list (their colliders are handled via the MeshFilter path).
            var meshFilterGOs = new HashSet<GameObject>();
            foreach (var go in Selection.gameObjects)
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                {
                    meshFilterGOs.Add(mf.gameObject);
                    if (_seenSet.Add(mf))
                        _sources.Add(mf);
                }

            foreach (var go in Selection.gameObjects)
                foreach (var col in go.GetComponentsInChildren<Collider>(true))
                    if (!meshFilterGOs.Contains(col.gameObject) && _seenColliderSet.Add(col.gameObject))
                        _colliderOnlySources.Add(col.gameObject);

            _particleSystemSources.Clear();
            _seenParticleSet.Clear();
            foreach (var go in Selection.gameObjects)
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                    if (!meshFilterGOs.Contains(ps.gameObject)
                        && ps.GetComponent<UdonSharpBehaviour>() == null
                        && _seenParticleSet.Add(ps.gameObject))
                        _particleSystemSources.Add(ps);

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
            // Clear previous status so a slow combine does not show stale results.
            _statusMessage = null;

            // Group all undo operations so a single Ctrl+Z reverses the entire combine.
            Undo.SetCurrentGroupName("Combine Meshes");
            var undoGroup = Undo.GetCurrentGroup();

            Mesh visualMesh = null;
            Mesh collisionMesh = null;
            // Declared outside try so catch can clean it up if an exception fires after creation.
            GameObject go = null;
            try
            {
                var result = MeshCombinerTool.Combine(valid, _colliderOnlySources, _root, new MeshCombinerTool.CombineOptions
                {
                    RecalculateNormals = _recalculateNormals,
                    ExcludeEditorOnly = _excludeEditorOnly,
                    IncludeColliders = _combineColliders,
                });

                visualMesh = result.VisualMesh;
                collisionMesh = result.CollisionMesh;

                SaveAsset(visualMesh, _savePath);
                if (collisionMesh != null)
                {
                    // Slice the last 6 chars (".asset") to avoid Replace hitting any earlier occurrence.
                    var collisionPath = _savePath[..^6] + "_Collider.asset";
                    SaveAsset(collisionMesh, collisionPath);
                }

                go = new GameObject("CombinedMesh");
                Undo.RegisterCreatedObjectUndo(go, "Combine Meshes");

                if (_root != null)
                    go.transform.SetParent(_root, worldPositionStays: false);

                go.AddComponent<MeshFilter>().sharedMesh = visualMesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = result.Materials;

                if (collisionMesh != null)
                    go.AddComponent<MeshCollider>().sharedMesh = collisionMesh;

                // Recreate primitive colliders as child GameObjects to preserve orientation.
                // Parent first so we can correct localScale relative to the parent's world scale.
                var parentScale = go.transform.lossyScale;
                foreach (var col in result.PrimitiveColliders)
                {
                    var child = new GameObject(col.gameObject.name + "_Collider");
                    Undo.RegisterCreatedObjectUndo(child, "Combine Meshes");
                    child.transform.SetPositionAndRotation(col.transform.position, col.transform.rotation);
                    child.transform.SetParent(go.transform, worldPositionStays: true);

                    // Compute localScale so world scale matches the original after reparenting.
                    // SetParent(worldPositionStays: true) only preserves position and rotation, not scale.
                    var targetScale = col.transform.lossyScale;
                    child.transform.localScale = new Vector3(
                        parentScale.x != 0f ? targetScale.x / parentScale.x : targetScale.x,
                        parentScale.y != 0f ? targetScale.y / parentScale.y : targetScale.y,
                        parentScale.z != 0f ? targetScale.z / parentScale.z : targetScale.z);

                    CopyCollider(col, child);
                }

                // Unity blocks SetParent on transforms inside a prefab instance, so unpack the enclosing
                // hierarchy layer by layer until the GO is free to reparent.
                int psCount = 0;
                if (_combineParticleSystems)
                {
                    foreach (var ps in _particleSystemSources)
                    {
                        if (ps == null) continue;
                        var prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(ps.gameObject);
                        while (prefabRoot != null)
                        {
                            PrefabUtility.UnpackPrefabInstance(prefabRoot, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
                            prefabRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(ps.gameObject);
                        }
                        Undo.RecordObject(ps.transform, "Combine Meshes");
                        ps.transform.SetParent(go.transform, worldPositionStays: true);
                        psCount++;
                    }
                }

                // Build a set of reparented GOs so the collider-only deactivation pass does not
                // deactivate GOs that were just moved under the combined output.
                var reparentedGOs = new HashSet<GameObject>();
                if (_combineParticleSystems)
                    foreach (var ps in _particleSystemSources)
                        if (ps != null) reparentedGOs.Add(ps.gameObject);

                int skippedUdon = 0;
                if (_deactivateSources)
                {
                    foreach (var mf in valid)
                    {
                        if (mf.GetComponent<UdonSharpBehaviour>() != null)
                        {
                            // Keep the source active so the Udon script and its collider keep running.
                            // Disabling the MeshRenderer hides rendering without affecting physics or script execution.
                            var mr = mf.GetComponent<MeshRenderer>();
                            if (mr != null)
                            {
                                Undo.RecordObject(mr, "Combine Meshes");
                                mr.enabled = false;
                            }
                            skippedUdon++;
                            continue;
                        }
                        Undo.RecordObject(mf.gameObject, "Combine Meshes");
                        mf.gameObject.SetActive(false);
                    }

                    foreach (var colGO in _colliderOnlySources)
                    {
                        if (colGO == null) continue;
                        // Only deactivate collider-only GOs when their colliders were actually combined.
                        // Deactivating them without combining their colliders would remove physics with no replacement.
                        if (!_combineColliders) continue;
                        // Skip GOs that were reparented for particle systems — they were moved, not left in place.
                        if (reparentedGOs.Contains(colGO)) continue;
                        if (colGO.GetComponent<UdonSharpBehaviour>() != null) { skippedUdon++; continue; }
                        Undo.RecordObject(colGO, "Combine Meshes");
                        colGO.SetActive(false);
                    }
                }

                // Collapse all registered undo operations into the single group opened at the top.
                // Without this, every RegisterCreatedObjectUndo and RecordObject is a separate step.
                Undo.CollapseUndoOperations(undoGroup);
                Selection.activeGameObject = go;

                _statusMessage = $"Done: {visualMesh.vertexCount:N0} vertices, {result.Materials.Length} material(s) saved to {_savePath}"
                    + (psCount > 0 ? $"\n{psCount} particle system(s) reparented." : "")
                    + (skippedUdon > 0 ? $"\n{skippedUdon} source(s) with UdonSharpBehaviour kept active." : "");
                _statusType = MessageType.Info;
                Debug.Log($"[Tsvrc] Mesh combined to {_savePath} ({result.Materials.Length} material(s), {visualMesh.vertexCount} vertices)"
                    + (psCount > 0 ? $" {psCount} particle system(s) reparented." : "")
                    + (skippedUdon > 0 ? $" {skippedUdon} Udon source(s) kept active." : ""));
            }
            catch (System.Exception ex)
            {
                // Destroy the combined GameObject if it was created before the exception.
                // DestroyImmediate removes it from the scene immediately; the dangling
                // RegisterCreatedObjectUndo entry becomes a no-op, which is acceptable.
                if (go != null) Object.DestroyImmediate(go);
                if (visualMesh != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(visualMesh)))
                    Object.DestroyImmediate(visualMesh);
                if (collisionMesh != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(collisionMesh)))
                    Object.DestroyImmediate(collisionMesh);

                _statusMessage = $"Combine failed: {ex.Message}";
                _statusType = MessageType.Error;
                Debug.LogException(ex);
            }
        }

        /// <summary>Saves <paramref name="asset"/> to <paramref name="path"/>, overwriting any existing asset. Creates the parent directory if it does not exist.</summary>
        private static void SaveAsset(Object asset, string path)
        {
            var dir = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
                System.IO.Directory.CreateDirectory(dir);

            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
                AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);
        }

        /// <summary>Copies collider properties from <paramref name="source"/> onto a new component added to <paramref name="target"/>.</summary>
        private static void CopyCollider(Collider source, GameObject target)
        {
            if (source is BoxCollider box)
            {
                var c = target.AddComponent<BoxCollider>();
                c.center = box.center;
                c.size = box.size;
                c.isTrigger = box.isTrigger;
                c.sharedMaterial = box.sharedMaterial;
            }
            else if (source is SphereCollider sphere)
            {
                var c = target.AddComponent<SphereCollider>();
                c.center = sphere.center;
                c.radius = sphere.radius;
                c.isTrigger = sphere.isTrigger;
                c.sharedMaterial = sphere.sharedMaterial;
            }
            else if (source is CapsuleCollider capsule)
            {
                var c = target.AddComponent<CapsuleCollider>();
                c.center = capsule.center;
                c.radius = capsule.radius;
                c.height = capsule.height;
                c.direction = capsule.direction;
                c.isTrigger = capsule.isTrigger;
                c.sharedMaterial = capsule.sharedMaterial;
            }
            else if (source is MeshCollider mc)
            {
                var c = target.AddComponent<MeshCollider>();
                // Both cookingOptions and convex must be assigned before sharedMesh because Unity
                // runs PhysX mesh cooking the moment sharedMesh is set, using whatever options are current.
                c.cookingOptions = mc.cookingOptions;
                c.convex = mc.convex;
                c.sharedMesh = mc.sharedMesh;
                c.isTrigger = mc.isTrigger;
                c.sharedMaterial = mc.sharedMaterial;
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

        /// <summary>Returns the current save path validation error, recomputed once per repaint and cached.</summary>
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
