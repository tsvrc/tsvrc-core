#if UNITY_EDITOR
using System;
using System.Linq;
using Tsvrc.Core;
using UdonSharp;
using UnityEditor;
using UnityEngine;

namespace Tsvrc.Editor.V2
{
    // One-shot copy of V1's real config data into V2's config containers. Triggered manually
    // from the Tsvrc > Configure window's "Migrate from V1" button - not run automatically,
    // since it overwrites whatever is currently there.
    //
    // Everything (Singletons, PooledObjects, Constructs, Factories) goes onto the single scene
    // TsvrcConfig (see its own doc comment for why it's a scene component and not an asset).
    // The library-internal/builtin side (TsvrcBuiltinConfig) is a separate genuine asset and is
    // migrated from V1's InternalTsvrcConfig asset separately (Singletons, PoolPrefabs, Factories).
    // V1's TsvrcConfig.Instance has no V2 equivalent field — InstanceModule auto-detects the
    // TsvrcInstance subclass in the project, so no explicit migration is needed.
    //
    // Mutates exclusively through SerializedObject/SerializedProperty, never raw field
    // assignment, consistent with every other mutation in this codebase.
    internal static class V1ConfigMigrator
    {
        private const string V1InternalConfigPath = "Assets/Tsvrc/InternalConfig.asset";
        private const string BuiltinConfigPath = "Assets/Tsvrc/TsvrcBuiltinConfig.asset";

        internal static void Migrate()
        {
            MigrateConfig();
            MigrateBuiltinConfig();

            AssetDatabase.SaveAssets();
            Debug.Log("[V1ConfigMigrator] Migration from V1 complete.");
        }

        private static void MigrateConfig()
        {
            var v1Config = FindV1Config();
            if (v1Config == null)
            {
                Debug.LogWarning("[V1ConfigMigrator] No V1 TsvrcConfig found in the scene. Nothing migrated.");
                return;
            }

            var v2Config = UnityEngine.Object.FindObjectOfType<TsvrcConfig>(true);
            if (v2Config == null)
            {
                Debug.LogWarning("[V1ConfigMigrator] No V2 TsvrcConfig found in the scene (run Tsvrc > Generate at least once first). Nothing migrated.");
                return;
            }

            var pooled = v1Config.PooledObjects ?? Array.Empty<UnityEngine.Object>();
            var pooledUsb = pooled.OfType<UdonSharpBehaviour>().ToArray();
            if (pooledUsb.Length != pooled.Length)
                Debug.LogWarning($"[V1ConfigMigrator] {pooled.Length - pooledUsb.Length} PooledObjects entr(y/ies) are not UdonSharpBehaviour and were dropped (V2 pool entries must be UdonSharpBehaviour prefabs).");

            var so = new SerializedObject(v2Config);
            SetObjectArray(so, "Singletons", v1Config.Singletons ?? Array.Empty<UnityEngine.Object>());
            SetObjectArray(so, "PooledObjects", pooledUsb);
            SetObjectArray(so, "Constructs", v1Config.TsvrcBehaviourConstruct ?? Array.Empty<TsvrcBehaviour>());
            SetObjectArray(so, "Factories", v1Config.Factories ?? Array.Empty<TsvrcFactoryGroup>(),
                (element, factory) =>
                {
                    element.FindPropertyRelative("GroupName").stringValue = factory.GroupName;
                    var prefabsProp = element.FindPropertyRelative("Prefabs");
                    SetObjectArray(prefabsProp, factory.Prefabs ?? Array.Empty<UnityEngine.Object>());
                });
            so.ApplyModifiedProperties();
        }

        private static void MigrateBuiltinConfig()
        {
            var v1Internal = AssetDatabase.LoadAssetAtPath<InternalTsvrcConfig>(V1InternalConfigPath);
            if (v1Internal == null)
            {
                Debug.LogWarning($"[V1ConfigMigrator] No V1 internal config found at {V1InternalConfigPath}. Builtin Singletons/PoolPrefabs not migrated.");
                return;
            }

            var v2Builtin = AssetDatabase.LoadAssetAtPath<TsvrcBuiltinConfig>(BuiltinConfigPath);
            if (v2Builtin == null)
            {
                v2Builtin = ScriptableObject.CreateInstance<TsvrcBuiltinConfig>();
                AssetDatabase.CreateAsset(v2Builtin, BuiltinConfigPath);
            }

            var so = new SerializedObject(v2Builtin);
            SetObjectArray(so, "Singletons", v1Internal.Singletons ?? Array.Empty<UnityEngine.Object>());
            SetObjectArray(so, "PoolPrefabs", v1Internal.PoolPrefabs ?? Array.Empty<TsvrcProcess>());
            SetObjectArray(so, "Factories", v1Internal.Factories ?? Array.Empty<TsvrcFactoryGroup>(),
                (element, factory) =>
                {
                    element.FindPropertyRelative("GroupName").stringValue = factory.GroupName;
                    var prefabsProp = element.FindPropertyRelative("Prefabs");
                    SetObjectArray(prefabsProp, factory.Prefabs ?? Array.Empty<UnityEngine.Object>());
                });
            so.ApplyModifiedProperties();
        }

        // Resizes propertyName's array to match values.Length and assigns each element by
        // reference. Plain reference-type arrays only (object/asset references).
        private static void SetObjectArray(SerializedObject so, string propertyName, UnityEngine.Object[] values)
            => SetObjectArray(so.FindProperty(propertyName), values);

        private static void SetObjectArray(SerializedProperty prop, UnityEngine.Object[] values)
        {
            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        // Overload for arrays of plain serialized structs/classes (not object references),
        // configuring each element via the supplied setter.
        private static void SetObjectArray<T>(SerializedObject so, string propertyName, T[] values, Action<SerializedProperty, T> setElement)
        {
            var prop = so.FindProperty(propertyName);
            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                setElement(prop.GetArrayElementAtIndex(i), values[i]);
        }

        private static Tsvrc.Core.TsvrcConfig FindV1Config()
        {
            var all = UnityEngine.Object.FindObjectsOfType<Tsvrc.Core.TsvrcConfig>(true);
            if (all.Length == 0) return null;
            if (all.Length > 1)
                Debug.LogWarning("[V1ConfigMigrator] Multiple V1 TsvrcConfig found in the scene; using the first.");
            return all[0];
        }
    }
}
#endif
