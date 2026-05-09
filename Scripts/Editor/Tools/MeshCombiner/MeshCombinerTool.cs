#if UNITY_EDITOR
using System.Collections.Generic;
using UdonSharp;
using UnityEngine;
using UnityEngine.Rendering;

namespace Tsvrc.Editor
{
    /// <summary>
    /// Combines visual meshes from multiple MeshFilter sources into a single multi-material mesh.
    /// Optionally merges solid MeshColliders and collects primitive and trigger colliders in the same pass.
    /// </summary>
    internal static class MeshCombinerTool
    {
        /// <summary>Controls how sources are combined.</summary>
        internal struct CombineOptions
        {
            /// <summary>Recomputes normals from geometry after combining. Disable to keep baked or hand-authored normals.</summary>
            public bool RecalculateNormals;
            /// <summary>Skips source GameObjects tagged "EditorOnly".</summary>
            public bool ExcludeEditorOnly;
            /// <summary>Merges solid MeshColliders and collects primitive and trigger colliders for the caller to reconstruct.</summary>
            public bool IncludeColliders;
        }

        /// <summary>Output produced by <see cref="Combine"/>.</summary>
        internal struct CombineResult
        {
            /// <summary>The combined visual mesh. Always non-null on success.</summary>
            public Mesh VisualMesh;
            /// <summary>Materials in submesh order, matching the submesh layout of <see cref="VisualMesh"/>.</summary>
            public Material[] Materials;
            /// <summary>Merged collision mesh from all solid non-trigger MeshColliders. Null when none were found or IncludeColliders is false.</summary>
            public Mesh CollisionMesh;
            /// <summary>Box, Sphere, Capsule, and trigger MeshColliders that need to be recreated as separate GameObjects. Empty when IncludeColliders is false.</summary>
            public List<Collider> PrimitiveColliders;
        }

        /// <summary>
        /// Combines all sources into a single visual mesh grouped by material, with optional collider handling.
        /// Solid MeshColliders are merged into one collision mesh. Primitive and trigger MeshColliders
        /// are returned in <see cref="CombineResult.PrimitiveColliders"/> for the caller to reconstruct as child GameObjects.
        /// Sources with an UdonSharpBehaviour are excluded from collider collection so their physics events keep working.
        /// </summary>
        /// <param name="filters">Source MeshFilters to combine.</param>
        /// <param name="root">Transform that defines the local space of the output mesh. Pass null to use world space.</param>
        /// <param name="options">Options controlling normalization, filtering, and collider handling.</param>
        /// <returns>Combined meshes and materials ready to be applied to a MeshFilter and MeshRenderer.</returns>
        /// <exception cref="System.InvalidOperationException">Thrown when no renderable submeshes are found in the sources.</exception>
        internal static CombineResult Combine(IList<MeshFilter> filters, Transform root, CombineOptions options)
        {
            var rootInverse = root != null ? root.worldToLocalMatrix : Matrix4x4.identity;

            var byMaterial = new Dictionary<Material, List<CombineInstance>>();
            var materialOrder = new List<Material>();

            var collisionInstances = options.IncludeColliders ? new List<CombineInstance>() : null;
            var primitives = options.IncludeColliders ? new List<Collider>() : null;

            foreach (var filter in filters)
            {
                if (filter == null || filter.sharedMesh == null) continue;
                if (options.ExcludeEditorOnly && filter.CompareTag("EditorOnly")) continue;

                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null) continue;

                var sharedMesh = filter.sharedMesh;
                var materials = renderer.sharedMaterials;
                var matrix = rootInverse * filter.transform.localToWorldMatrix;

                for (int sub = 0; sub < sharedMesh.subMeshCount; sub++)
                {
                    // Use the last material for any submesh beyond the array length, matching how MeshRenderer handles this.
                    var mat = materials.Length > 0
                        ? materials[Mathf.Min(sub, materials.Length - 1)]
                        : null;

                    if (mat == null) continue;

                    if (!byMaterial.TryGetValue(mat, out var list))
                    {
                        list = new List<CombineInstance>(capacity: 8);
                        byMaterial[mat] = list;
                        materialOrder.Add(mat);
                    }

                    list.Add(new CombineInstance { mesh = sharedMesh, subMeshIndex = sub, transform = matrix });
                }

                // Colliders are processed here to avoid a second pass over the sources.
                // Sources with an UdonSharpBehaviour are skipped so their physics events keep firing correctly.
                if (options.IncludeColliders && filter.GetComponent<UdonSharpBehaviour>() == null)
                {
                    foreach (var col in filter.GetComponents<Collider>())
                    {
                        // Disabled colliders don't participate in physics. Recreating them as enabled
                        // would silently change scene behavior.
                        if (!col.enabled) continue;

                        if (col is MeshCollider mc)
                        {
                            // Trigger MeshColliders cannot be merged into the solid collision mesh because
                            // the trigger flag would be lost. They are recreated as separate child GameObjects.
                            // Trigger colliders with no mesh assigned are skipped since they have no valid physics shape.
                            if (mc.isTrigger)
                            {
                                if (mc.sharedMesh != null)
                                    primitives.Add(col);
                            }
                            else if (mc.sharedMesh != null)
                            {
                                collisionInstances.Add(new CombineInstance { mesh = mc.sharedMesh, transform = matrix });
                            }
                        }
                        else
                        {
                            primitives.Add(col);
                        }
                    }
                }
            }

            if (materialOrder.Count == 0)
                throw new System.InvalidOperationException(
                    "No renderable submeshes found. Ensure sources have a MeshRenderer with at least one assigned material.");

            // The visual mesh uses two passes: first merge all instances per material into flat meshes,
            // then combine those as separate submeshes to keep material boundaries intact.
            // Both meshes are built inside the same try block so a failure cleans up both.
            var perMaterialMeshes = new CombineInstance[materialOrder.Count];
            var tempMeshes = new Mesh[materialOrder.Count];
            var visualMesh = new Mesh { name = "CombinedMesh", indexFormat = IndexFormat.UInt32 };
            Mesh collisionMesh = null;

            bool succeeded = false;
            try
            {
                for (int i = 0; i < materialOrder.Count; i++)
                {
                    var sub = new Mesh { indexFormat = IndexFormat.UInt32 };
                    sub.CombineMeshes(byMaterial[materialOrder[i]].ToArray(), mergeSubMeshes: true, useMatrices: true);
                    perMaterialMeshes[i] = new CombineInstance { mesh = sub, transform = Matrix4x4.identity };
                    tempMeshes[i] = sub;
                }

                visualMesh.CombineMeshes(perMaterialMeshes, mergeSubMeshes: false, useMatrices: false);
                if (options.RecalculateNormals)
                    visualMesh.RecalculateNormals();
                visualMesh.RecalculateBounds();
                visualMesh.Optimize();

                // Collision mesh: flat single-submesh merge, no material grouping needed.
                if (options.IncludeColliders && collisionInstances.Count > 0)
                {
                    collisionMesh = new Mesh { name = "CombinedCollisionMesh", indexFormat = IndexFormat.UInt32 };
                    collisionMesh.CombineMeshes(collisionInstances.ToArray(), mergeSubMeshes: true, useMatrices: true);
                    collisionMesh.RecalculateBounds();
                    collisionMesh.Optimize();
                }

                succeeded = true;
            }
            finally
            {
                foreach (var m in tempMeshes)
                    if (m != null) Object.DestroyImmediate(m);

                if (!succeeded)
                {
                    Object.DestroyImmediate(visualMesh);
                    if (collisionMesh != null) Object.DestroyImmediate(collisionMesh);
                }
            }

            return new CombineResult
            {
                VisualMesh = visualMesh,
                Materials = materialOrder.ToArray(),
                CollisionMesh = collisionMesh,
                PrimitiveColliders = primitives ?? new List<Collider>(),
            };
        }
    }
}
#endif
