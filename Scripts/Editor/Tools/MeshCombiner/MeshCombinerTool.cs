#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Tsvrc.Editor
{
    /// <summary>
    /// Pure logic for combining meshes from a set of MeshFilters into a single mesh.
    /// Multi-material is preserved: one submesh is produced per unique material.
    /// </summary>
    internal static class MeshCombinerTool
    {
        internal struct CombineResult
        {
            public Mesh Mesh;
            public Material[] Materials;
        }

        /// <param name="filters">Source MeshFilters. Null entries are skipped.</param>
        /// <param name="root">
        /// The transform that the combined mesh will be placed on.
        /// Vertices are expressed in root's local space.
        /// Pass null to use world space.
        /// </param>
        /// <param name="recalculateNormals">
        /// Recompute normals from geometry after combining.
        /// Disable this to preserve baked/hand-authored normals from the source meshes.
        /// </param>
        internal static CombineResult Combine(IList<MeshFilter> filters, Transform root, bool recalculateNormals = false)
        {
            var rootInverse = root != null ? root.worldToLocalMatrix : Matrix4x4.identity;

            // material → list of CombineInstances (one per submesh)
            var byMaterial = new Dictionary<Material, List<CombineInstance>>();
            var materialOrder = new List<Material>(); // preserve insertion order

            foreach (var filter in filters)
            {
                if (filter == null || filter.sharedMesh == null) continue;

                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null) continue;

                var sharedMesh = filter.sharedMesh;
                var materials = renderer.sharedMaterials;
                var matrix = rootInverse * filter.transform.localToWorldMatrix;

                for (int sub = 0; sub < sharedMesh.subMeshCount; sub++)
                {
                    // clamp: if fewer materials than submeshes, reuse the last one
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

                    list.Add(new CombineInstance
                    {
                        mesh = sharedMesh,
                        subMeshIndex = sub,
                        transform = matrix,
                    });
                }
            }

            // Pass 1 — combine all instances that share a material into one mesh each.
            var perMaterialMeshes = new CombineInstance[materialOrder.Count];
            var tempMeshes = new Mesh[materialOrder.Count];
            var combined = new Mesh
            {
                name = "CombinedMesh",
                indexFormat = IndexFormat.UInt32,
            };

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

                // Pass 2 — merge per-material meshes, keeping submesh boundaries.
                combined.CombineMeshes(perMaterialMeshes, mergeSubMeshes: false, useMatrices: false);
                if (recalculateNormals)
                    combined.RecalculateNormals();
                combined.RecalculateBounds();
                combined.Optimize();
                succeeded = true;
            }
            finally
            {
                // Always destroy temp meshes — even if an exception is thrown above.
                foreach (var m in tempMeshes)
                    if (m != null) Object.DestroyImmediate(m);

                // Destroy the output mesh too if we never finished building it.
                if (!succeeded)
                    Object.DestroyImmediate(combined);
            }

            return new CombineResult
            {
                Mesh = combined,
                Materials = materialOrder.ToArray(),
            };
        }
    }
}
#endif
