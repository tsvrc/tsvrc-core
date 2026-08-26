#if UNITY_EDITOR
using UnityEditor;

namespace Tsvrc.Editor
{
    // Batches edits to a TsConfig/TsBuiltinConfig behind an explicit Apply/Discard step, instead
    // of every SerializedObject.ApplyModifiedProperties() call immediately triggering a real
    // regenerate pass via TsGenerator's Undo.postprocessModifications watch. One instance per
    // editing surface (TsWindow owns one per TsConfig, TsBuiltinConfigInspector owns one per
    // TsBuiltinConfig instance being inspected).
    internal sealed class TsPendingConfigEdit
    {
        private UnityEngine.Object _target;
        private string _baselineJson;

        // A hidden clone of _target, used only to restore on Discard - EditorJsonUtility.
        // FromJsonOverwrite does not reliably round-trip UnityEngine.Object references (found via
        // a failing test), so Discard instead uses EditorUtility.CopySerialized against this.
        private UnityEngine.Object _baselineClone;
        private UnityEngine.GameObject _baselineCloneHost;

        private System.IDisposable _suppression;

        internal bool HasPendingChanges { get; private set; }

        // Rebaselines only when target's identity actually changes (a new TsConfig found, a
        // different TsBuiltinConfig selected) - never on every OnGUI call.
        internal void BeginTracking(UnityEngine.Object target)
        {
            if (ReferenceEquals(target, _target)) return;

            DestroyBaselineClone();
            _target = target;
            HasPendingChanges = false;
            // Re-armed immediately rather than left null until the caller's next BeginFrame():
            // BeginTracking can run outside OnGUI (e.g. via TsGenerator.StateChanged, fired by a
            // regenerate completing), and leaving suppression down until the next OnGUI call
            // would open a real window for an automatic trigger to slip through unsuppressed.
            _suppression?.Dispose();
            _suppression = _target != null ? TsGenerator.SuppressAutomaticTriggers() : null;

            if (_target != null)
            {
                _baselineJson = EditorJsonUtility.ToJson(_target);
                _baselineClone = CreateSameTypeInstance(_target, out _baselineCloneHost);
                EditorUtility.CopySerialized(_target, _baselineClone);
            }
            else
            {
                _baselineJson = null;
            }
        }

        // Arms suppression proactively, before any edit could happen this frame - some callers
        // (TsGroupTreeGUI's drag-and-drop reparenting) call SerializedObject.
        // ApplyModifiedProperties() themselves mid-draw, ahead of the caller's own outer call.
        internal void BeginFrame()
        {
            if (_target == null) return;
            if (_suppression == null)
                _suppression = TsGenerator.SuppressAutomaticTriggers();
        }

        // Call right after SerializedObject.ApplyModifiedProperties() each OnGUI pass.
        // anyChangesApplied should be that call's own return value combined with
        // EditorGUI.EndChangeCheck() (see TsWindow.OnGUI) - the diff below is skipped whenever
        // nothing was applied, since running EditorJsonUtility.ToJson() over the whole tracked
        // object on every single OnGUI event (including pure-navigation events like scrolling,
        // where nothing changed at all) was measurably degrading scroll smoothness on a config
        // with a non-trivial entry count.
        //
        // Skipping the diff must never also skip releasing suppression on a "nothing changed"
        // frame, or a window that's simply open (no edits ever made) would hold TsGenerator
        // suppression permanently after its first draw. Release is keyed off HasPendingChanges
        // directly, not off whether this frame's diff ran.
        internal void NotifyAppliedToSerializedObject(bool anyChangesApplied)
        {
            if (_target == null) return;

            if (anyChangesApplied)
            {
                string currentJson = EditorJsonUtility.ToJson(_target);
                HasPendingChanges = HasChanged(_baselineJson, currentJson);
            }

            if (!HasPendingChanges)
            {
                _suppression?.Dispose();
                _suppression = null;
            }
        }

        // Pure so it's directly unit-testable.
        internal static bool HasChanged(string baselineJson, string currentJson) => baselineJson != currentJson;

        // Ends suppression (letting the one regenerate pass this edit set accumulated actually
        // run), fires it explicitly, then rebaselines against the now-applied state.
        internal void Apply()
        {
            if (_target == null) return;

            _suppression?.Dispose();
            _suppression = null;
            TsGenerator.ScheduleRerun();

            _baselineJson = EditorJsonUtility.ToJson(_target);
            EditorUtility.CopySerialized(_target, _baselineClone);
            HasPendingChanges = false;
        }

        // Reverts _target to the last-applied state without regenerating, then rebaselines and
        // ends suppression - a discarded edit never reaches ScheduleRerun() at all.
        internal void Discard(SerializedObject so)
        {
            if (_target == null) return;

            EditorUtility.CopySerialized(_baselineClone, _target);
            so?.Update();

            _suppression?.Dispose();
            _suppression = null;
            HasPendingChanges = false;
        }

        // Called from OnDestroy (not OnDisable, which also fires around a domain reload) - only
        // releases the hidden baseline-clone GameObject and any held suppression scope, never
        // loses data: by the time OnDestroy runs, hasUnsavedChanges/SaveChanges/DiscardChanges
        // have already resolved any pending edit through Unity's own dialog.
        internal void Cleanup()
        {
            _suppression?.Dispose();
            _suppression = null;
            DestroyBaselineClone();
        }

        private void DestroyBaselineClone()
        {
            if (_baselineCloneHost != null)
                UnityEngine.Object.DestroyImmediate(_baselineCloneHost);
            else if (_baselineClone != null)
                UnityEngine.Object.DestroyImmediate(_baselineClone);
            _baselineClone = null;
            _baselineCloneHost = null;
        }

        // A Component needs a (hidden, editor-only) host GameObject to live on; a ScriptableObject
        // can be instantiated directly. host is non-null only for the Component case, so Cleanup
        // knows which one to destroy.
        private static UnityEngine.Object CreateSameTypeInstance(UnityEngine.Object target, out UnityEngine.GameObject host)
        {
            if (target is UnityEngine.Component)
            {
                host = new UnityEngine.GameObject("TsPendingConfigEdit baseline")
                {
                    hideFlags = UnityEngine.HideFlags.HideAndDontSave,
                };
                return host.AddComponent(target.GetType());
            }

            host = null;
            return UnityEngine.ScriptableObject.CreateInstance(target.GetType());
        }
    }
}
#endif
