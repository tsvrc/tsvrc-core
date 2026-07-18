using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Editor;
using UdonSharp;
using UnityEngine;
using UnityEngine.Rendering;

namespace Tsvrc.Tests.EditMode
{
    // MeshCombinerTool is internal static, but AssemblyInfo.cs grants Tsvrc.Tests.EditMode
    // InternalsVisibleTo access to Tsvrc.Editor, so it (and its nested CombineOptions/
    // CombineResult structs) can be called directly, no reflection needed.
    public class MeshCombinerToolTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly List<Object> _assets = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);
            _spawned.Clear();

            foreach (Object asset in _assets)
                if (asset != null)
                    Object.DestroyImmediate(asset);
            _assets.Clear();
        }

        private GameObject CreateCubeSource(string name, Material material, bool active = true)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            _spawned.Add(go);
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            // CreatePrimitive adds a BoxCollider by default; tests that don't want one remove it explicitly.
            if (!active) go.SetActive(false);
            return go;
        }

        private static Material NewMaterial(string name)
        {
            var mat = new Material(Shader.Find("Standard")) { name = name };
            return mat;
        }

        private Mesh SharedCubeMesh(GameObject cubeGO) => cubeGO.GetComponent<MeshFilter>().sharedMesh;

        private static MeshCombinerTool.CombineOptions DefaultOptions => new MeshCombinerTool.CombineOptions
        {
            RecalculateNormals = false,
            ExcludeEditorOnly = false,
            IncludeColliders = false,
        };

        [Test]
        public void Combine_SingleSource_ProducesOneSubmeshWithThatMaterial()
        {
            Material mat = NewMaterial("A");
            _assets.Add(mat);
            GameObject cube = CreateCubeSource("Cube", mat);

            var result = MeshCombinerTool.Combine(new[] { cube.GetComponent<MeshFilter>() }, null, null, DefaultOptions);
            _assets.Add(result.VisualMesh);

            Assert.AreEqual(1, result.Materials.Length);
            Assert.AreSame(mat, result.Materials[0]);
            Assert.AreEqual(1, result.VisualMesh.subMeshCount);
            Assert.Greater(result.VisualMesh.vertexCount, 0);
        }

        [Test]
        public void Combine_TwoSourcesSameMaterial_MergeIntoOneSubmesh()
        {
            Material mat = NewMaterial("Shared");
            _assets.Add(mat);
            GameObject cubeA = CreateCubeSource("A", mat);
            GameObject cubeB = CreateCubeSource("B", mat);
            cubeB.transform.position = new Vector3(5, 0, 0);

            var result = MeshCombinerTool.Combine(
                new[] { cubeA.GetComponent<MeshFilter>(), cubeB.GetComponent<MeshFilter>() }, null, null, DefaultOptions);
            _assets.Add(result.VisualMesh);

            Assert.AreEqual(1, result.Materials.Length);
            Assert.AreEqual(1, result.VisualMesh.subMeshCount);
            int singleCubeVerts = SharedCubeMesh(cubeA).vertexCount;
            Assert.AreEqual(singleCubeVerts * 2, result.VisualMesh.vertexCount);
        }

        [Test]
        public void Combine_TwoSourcesDifferentMaterials_ProduceSeparateSubmeshesInFirstSeenOrder()
        {
            Material matA = NewMaterial("A");
            Material matB = NewMaterial("B");
            _assets.Add(matA);
            _assets.Add(matB);
            GameObject cubeB = CreateCubeSource("B", matB);
            GameObject cubeA = CreateCubeSource("A", matA);

            var result = MeshCombinerTool.Combine(
                new[] { cubeB.GetComponent<MeshFilter>(), cubeA.GetComponent<MeshFilter>() }, null, null, DefaultOptions);
            _assets.Add(result.VisualMesh);

            Assert.AreEqual(2, result.Materials.Length);
            Assert.AreEqual(2, result.VisualMesh.subMeshCount);
            Assert.AreSame(matB, result.Materials[0], "Materials must be ordered by first appearance in the source list.");
            Assert.AreSame(matA, result.Materials[1]);
        }

        [Test]
        public void Combine_OnlySourceHasNullMaterial_ThrowsInvalidOperationException()
        {
            // A null material means that submesh is skipped entirely (not recorded with a
            // placeholder), so with no other sources, no submesh is ever recorded at all.
            GameObject cube = CreateCubeSource("Cube", null);

            Assert.Throws<System.InvalidOperationException>(() =>
                MeshCombinerTool.Combine(new[] { cube.GetComponent<MeshFilter>() }, null, null, DefaultOptions));
        }

        [Test]
        public void Combine_OneSourceHasNullMaterial_OnlyValidMaterialSourceIsIncluded()
        {
            Material mat = NewMaterial("A");
            _assets.Add(mat);
            GameObject withMat = CreateCubeSource("WithMat", mat);
            GameObject withoutMat = CreateCubeSource("WithoutMat", null);

            var result = MeshCombinerTool.Combine(
                new[] { withoutMat.GetComponent<MeshFilter>(), withMat.GetComponent<MeshFilter>() }, null, null, DefaultOptions);
            _assets.Add(result.VisualMesh);

            Assert.AreEqual(1, result.Materials.Length);
            Assert.AreSame(mat, result.Materials[0]);
        }

        [Test]
        public void Combine_NullFilterInList_IsSkipped()
        {
            Material mat = NewMaterial("A");
            _assets.Add(mat);
            GameObject cube = CreateCubeSource("Cube", mat);

            var result = MeshCombinerTool.Combine(
                new[] { null, cube.GetComponent<MeshFilter>() }, null, null, DefaultOptions);
            _assets.Add(result.VisualMesh);

            Assert.AreEqual(1, result.Materials.Length);
        }

        [Test]
        public void Combine_FilterWithNoMeshRenderer_IsSkipped()
        {
            var go = new GameObject("NoRenderer");
            _spawned.Add(go);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = new Mesh();
            _assets.Add(mf.sharedMesh);

            var ex = Assert.Throws<System.InvalidOperationException>(() =>
                MeshCombinerTool.Combine(new[] { mf }, null, null, DefaultOptions));
            StringAssert.Contains("No renderable submeshes found", ex.Message);
        }

        [Test]
        public void Combine_FilterWithNullSharedMesh_IsSkippedAmongOtherValidSources()
        {
            var noMeshGO = new GameObject("NoMesh");
            _spawned.Add(noMeshGO);
            var noMeshFilter = noMeshGO.AddComponent<MeshFilter>();
            noMeshGO.AddComponent<MeshRenderer>();
            // sharedMesh left null

            Material mat = NewMaterial("A");
            _assets.Add(mat);
            GameObject cube = CreateCubeSource("Cube", mat);

            var result = MeshCombinerTool.Combine(
                new[] { noMeshFilter, cube.GetComponent<MeshFilter>() }, null, null, DefaultOptions);
            _assets.Add(result.VisualMesh);

            Assert.AreEqual(1, result.Materials.Length);
            Assert.AreEqual(SharedCubeMesh(cube).vertexCount, result.VisualMesh.vertexCount,
                "The null-sharedMesh source contributes no geometry.");
        }

        [Test]
        public void Combine_SourceWithNoMeshRenderer_IsSkippedAmongOtherValidSources()
        {
            var noRendererGO = new GameObject("NoRenderer");
            _spawned.Add(noRendererGO);
            var noRendererFilter = noRendererGO.AddComponent<MeshFilter>();
            noRendererFilter.sharedMesh = new Mesh();
            _assets.Add(noRendererFilter.sharedMesh);

            Material mat = NewMaterial("A");
            _assets.Add(mat);
            GameObject cube = CreateCubeSource("Cube", mat);

            var result = MeshCombinerTool.Combine(
                new[] { noRendererFilter, cube.GetComponent<MeshFilter>() }, null, null, DefaultOptions);
            _assets.Add(result.VisualMesh);

            Assert.AreEqual(1, result.Materials.Length);
            Assert.AreEqual(SharedCubeMesh(cube).vertexCount, result.VisualMesh.vertexCount);
        }

        [Test]
        public void Combine_DuplicateFilterInList_ProducesDuplicatedGeometry()
        {
            // Documents current behavior: Combine does not dedupe its input — that's the
            // caller's responsibility (MeshCombinerWindow.GetValid() dedupes before
            // calling Combine). Passing the same MeshFilter twice doubles its contribution.
            Material mat = NewMaterial("A");
            _assets.Add(mat);
            GameObject cube = CreateCubeSource("Cube", mat);
            MeshFilter mf = cube.GetComponent<MeshFilter>();

            var result = MeshCombinerTool.Combine(new[] { mf, mf }, null, null, DefaultOptions);
            _assets.Add(result.VisualMesh);

            Assert.AreEqual(SharedCubeMesh(cube).vertexCount * 2, result.VisualMesh.vertexCount);
        }

        [Test]
        public void Combine_EmptySourcesAndNoColliderOnly_ThrowsInvalidOperationException()
        {
            Assert.Throws<System.InvalidOperationException>(() =>
                MeshCombinerTool.Combine(new MeshFilter[0], null, null, DefaultOptions));
        }

        [Test]
        public void Combine_ExcludeEditorOnly_SkipsTaggedSources()
        {
            Material mat = NewMaterial("A");
            _assets.Add(mat);
            GameObject cube = CreateCubeSource("Cube", mat);
            cube.tag = "EditorOnly";

            var options = DefaultOptions;
            options.ExcludeEditorOnly = true;

            Assert.Throws<System.InvalidOperationException>(() =>
                MeshCombinerTool.Combine(new[] { cube.GetComponent<MeshFilter>() }, null, null, options));
        }

        [Test]
        public void Combine_InactiveSource_StillIncluded()
        {
            // Documents current behavior: Combine has no GameObject.activeSelf/
            // activeInHierarchy check anywhere — an inactive source still contributes
            // geometry, unlike ExcludeEditorOnly which is an explicit opt-in filter.
            Material mat = NewMaterial("A");
            _assets.Add(mat);
            GameObject cube = CreateCubeSource("Cube", mat, active: false);

            var result = MeshCombinerTool.Combine(new[] { cube.GetComponent<MeshFilter>() }, null, null, DefaultOptions);
            _assets.Add(result.VisualMesh);

            Assert.AreEqual(1, result.Materials.Length);
            Assert.Greater(result.VisualMesh.vertexCount, 0);
        }

        [Test]
        public void Combine_ExcludeEditorOnlyFalse_IncludesTaggedSources()
        {
            Material mat = NewMaterial("A");
            _assets.Add(mat);
            GameObject cube = CreateCubeSource("Cube", mat);
            cube.tag = "EditorOnly";

            var result = MeshCombinerTool.Combine(new[] { cube.GetComponent<MeshFilter>() }, null, null, DefaultOptions);
            _assets.Add(result.VisualMesh);

            Assert.AreEqual(1, result.Materials.Length);
        }

        [Test]
        public void Combine_SubmeshCountExceedsMaterialCount_FallsBackToLastMaterial()
        {
            // A cube mesh has 1 submesh; assigning it a sharedMaterials array shorter than
            // subMeshCount never actually happens for a primitive cube (both are 1), so this
            // test instead verifies the *documented* fallback path directly: build a mesh
            // with 2 submeshes but only assign 1 material via sharedMaterials.
            var go = new GameObject("MultiSubmesh");
            _spawned.Add(go);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();

            Mesh cubeMesh = GameObject.CreatePrimitive(PrimitiveType.Cube).GetComponent<MeshFilter>().sharedMesh;
            var mesh = new Mesh();
            mesh.vertices = cubeMesh.vertices;
            mesh.subMeshCount = 2;
            mesh.SetTriangles(cubeMesh.triangles, 0);
            mesh.SetTriangles(cubeMesh.triangles, 1);
            mf.sharedMesh = mesh;
            _assets.Add(mesh);

            Material onlyMat = NewMaterial("Only");
            _assets.Add(onlyMat);
            mr.sharedMaterials = new[] { onlyMat };

            var result = MeshCombinerTool.Combine(new[] { mf }, null, null, DefaultOptions);
            _assets.Add(result.VisualMesh);

            Assert.AreEqual(1, result.Materials.Length, "Both submeshes fall back to the single available material and merge together.");
            Assert.AreSame(onlyMat, result.Materials[0]);
        }

        [Test]
        public void Combine_NullRoot_UsesWorldSpace()
        {
            Material mat = NewMaterial("A");
            _assets.Add(mat);
            GameObject cube = CreateCubeSource("Cube", mat);
            cube.transform.position = new Vector3(10, 0, 0);

            var result = MeshCombinerTool.Combine(new[] { cube.GetComponent<MeshFilter>() }, null, null, DefaultOptions);
            _assets.Add(result.VisualMesh);

            result.VisualMesh.RecalculateBounds();
            Assert.AreEqual(10f, result.VisualMesh.bounds.center.x, 0.01f);
        }

        [Test]
        public void Combine_WithRoot_ExpressesMeshInRootLocalSpace()
        {
            var rootGO = new GameObject("Root");
            _spawned.Add(rootGO);
            rootGO.transform.position = new Vector3(10, 0, 0);

            Material mat = NewMaterial("A");
            _assets.Add(mat);
            GameObject cube = CreateCubeSource("Cube", mat);
            cube.transform.position = new Vector3(10, 0, 0);

            var result = MeshCombinerTool.Combine(new[] { cube.GetComponent<MeshFilter>() }, null, rootGO.transform, DefaultOptions);
            _assets.Add(result.VisualMesh);

            result.VisualMesh.RecalculateBounds();
            Assert.AreEqual(0f, result.VisualMesh.bounds.center.x, 0.01f, "Cube sits at the root's origin, so local-space center should be ~0.");
        }

        [Test]
        public void Combine_IncludeCollidersFalse_ReturnsNoCollisionMeshAndEmptyPrimitives()
        {
            Material mat = NewMaterial("A");
            _assets.Add(mat);
            GameObject cube = CreateCubeSource("Cube", mat);
            Object.DestroyImmediate(cube.GetComponent<BoxCollider>());
            var meshCollider = cube.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = SharedCubeMesh(cube);

            var result = MeshCombinerTool.Combine(new[] { cube.GetComponent<MeshFilter>() }, null, null, DefaultOptions);
            _assets.Add(result.VisualMesh);

            Assert.IsNull(result.CollisionMesh);
            Assert.AreEqual(0, result.PrimitiveColliders.Count);
        }

        [Test]
        public void Combine_SolidMeshCollider_MergesIntoCollisionMesh()
        {
            Material mat = NewMaterial("A");
            _assets.Add(mat);
            GameObject cube = CreateCubeSource("Cube", mat);
            Object.DestroyImmediate(cube.GetComponent<BoxCollider>());
            var meshCollider = cube.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = SharedCubeMesh(cube);

            var options = DefaultOptions;
            options.IncludeColliders = true;
            var result = MeshCombinerTool.Combine(new[] { cube.GetComponent<MeshFilter>() }, null, null, options);
            _assets.Add(result.VisualMesh);
            if (result.CollisionMesh != null) _assets.Add(result.CollisionMesh);

            Assert.IsNotNull(result.CollisionMesh);
            Assert.Greater(result.CollisionMesh.vertexCount, 0);
            Assert.AreEqual(0, result.PrimitiveColliders.Count);
        }

        [Test]
        public void Combine_TriggerMeshCollider_RoutedToPrimitiveCollidersNotMerged()
        {
            Material mat = NewMaterial("A");
            _assets.Add(mat);
            GameObject cube = CreateCubeSource("Cube", mat);
            Object.DestroyImmediate(cube.GetComponent<BoxCollider>());
            var meshCollider = cube.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = SharedCubeMesh(cube);
            // Unity silently refuses isTrigger on a concave (non-convex) MeshCollider
            // ("Triggers on concave MeshColliders are not supported"), so convex must be
            // set first for the trigger flag to actually take.
            meshCollider.convex = true;
            meshCollider.isTrigger = true;

            var options = DefaultOptions;
            options.IncludeColliders = true;
            var result = MeshCombinerTool.Combine(new[] { cube.GetComponent<MeshFilter>() }, null, null, options);
            _assets.Add(result.VisualMesh);

            Assert.IsNull(result.CollisionMesh);
            Assert.AreEqual(1, result.PrimitiveColliders.Count);
            Assert.AreSame(meshCollider, result.PrimitiveColliders[0]);
        }

        [Test]
        public void Combine_BoxCollider_RoutedToPrimitiveColliders()
        {
            Material mat = NewMaterial("A");
            _assets.Add(mat);
            GameObject cube = CreateCubeSource("Cube", mat);
            BoxCollider box = cube.GetComponent<BoxCollider>();

            var options = DefaultOptions;
            options.IncludeColliders = true;
            var result = MeshCombinerTool.Combine(new[] { cube.GetComponent<MeshFilter>() }, null, null, options);
            _assets.Add(result.VisualMesh);

            Assert.AreEqual(1, result.PrimitiveColliders.Count);
            Assert.AreSame(box, result.PrimitiveColliders[0]);
        }

        [Test]
        public void Combine_DisabledCollider_IsSkipped()
        {
            Material mat = NewMaterial("A");
            _assets.Add(mat);
            GameObject cube = CreateCubeSource("Cube", mat);
            cube.GetComponent<BoxCollider>().enabled = false;

            var options = DefaultOptions;
            options.IncludeColliders = true;
            var result = MeshCombinerTool.Combine(new[] { cube.GetComponent<MeshFilter>() }, null, null, options);
            _assets.Add(result.VisualMesh);

            Assert.AreEqual(0, result.PrimitiveColliders.Count);
        }

        [Test]
        public void Combine_MeshColliderWithNullSharedMesh_IsSkippedEntirely()
        {
            Material mat = NewMaterial("A");
            _assets.Add(mat);
            GameObject cube = CreateCubeSource("Cube", mat);
            Object.DestroyImmediate(cube.GetComponent<BoxCollider>());
            var meshCollider = cube.AddComponent<MeshCollider>();
            // MeshCollider.Reset() (invoked by AddComponent) auto-populates sharedMesh
            // from the sibling MeshFilter, so it must be explicitly cleared to null here.
            meshCollider.sharedMesh = null;

            var options = DefaultOptions;
            options.IncludeColliders = true;
            var result = MeshCombinerTool.Combine(new[] { cube.GetComponent<MeshFilter>() }, null, null, options);
            _assets.Add(result.VisualMesh);

            Assert.IsNull(result.CollisionMesh);
            Assert.AreEqual(0, result.PrimitiveColliders.Count);
        }

        [Test]
        public void Combine_SourceWithUdonSharpBehaviour_ExcludedFromColliderCollectionButStillVisible()
        {
            Material mat = NewMaterial("A");
            _assets.Add(mat);
            GameObject cube = CreateCubeSource("Cube", mat);
            cube.AddComponent<MeshCombinerToolUdonTestBehaviour>();

            var options = DefaultOptions;
            options.IncludeColliders = true;
            var result = MeshCombinerTool.Combine(new[] { cube.GetComponent<MeshFilter>() }, null, null, options);
            _assets.Add(result.VisualMesh);

            Assert.AreEqual(1, result.Materials.Length, "Visual mesh still includes Udon-scripted sources.");
            Assert.AreEqual(0, result.PrimitiveColliders.Count, "Colliders on Udon-scripted sources are excluded so their physics events keep firing.");
        }

        [Test]
        public void Combine_MultipleCollidersOnSameSource_AllRoutedToPrimitives()
        {
            Material mat = NewMaterial("A");
            _assets.Add(mat);
            GameObject cube = CreateCubeSource("Cube", mat);
            BoxCollider box = cube.GetComponent<BoxCollider>();
            SphereCollider sphere = cube.AddComponent<SphereCollider>();

            var options = DefaultOptions;
            options.IncludeColliders = true;
            var result = MeshCombinerTool.Combine(new[] { cube.GetComponent<MeshFilter>() }, null, null, options);
            _assets.Add(result.VisualMesh);

            Assert.AreEqual(2, result.PrimitiveColliders.Count);
            CollectionAssert.Contains(result.PrimitiveColliders, box);
            CollectionAssert.Contains(result.PrimitiveColliders, sphere);
        }

        [Test]
        public void Combine_ColliderOnlySource_ContributesCollidersButNoVisualGeometry()
        {
            var colliderOnlyGO = new GameObject("ColliderOnly");
            _spawned.Add(colliderOnlyGO);
            colliderOnlyGO.AddComponent<BoxCollider>();

            var options = DefaultOptions;
            options.IncludeColliders = true;

            Material mat = NewMaterial("A");
            _assets.Add(mat);
            GameObject cube = CreateCubeSource("Cube", mat);
            Object.DestroyImmediate(cube.GetComponent<BoxCollider>());

            var result = MeshCombinerTool.Combine(
                new[] { cube.GetComponent<MeshFilter>() }, new[] { colliderOnlyGO }, null, options);
            _assets.Add(result.VisualMesh);

            Assert.AreEqual(1, result.PrimitiveColliders.Count);
        }

        [Test]
        public void Combine_ColliderOnlySourceWithUdonSharpBehaviour_IsExcludedFromColliders()
        {
            var colliderOnlyGO = new GameObject("ColliderOnly");
            _spawned.Add(colliderOnlyGO);
            colliderOnlyGO.AddComponent<BoxCollider>();
            colliderOnlyGO.AddComponent<MeshCombinerToolUdonTestBehaviour>();

            Material mat = NewMaterial("A");
            _assets.Add(mat);
            GameObject cube = CreateCubeSource("Cube", mat);
            Object.DestroyImmediate(cube.GetComponent<BoxCollider>());

            var options = DefaultOptions;
            options.IncludeColliders = true;
            var result = MeshCombinerTool.Combine(
                new[] { cube.GetComponent<MeshFilter>() }, new[] { colliderOnlyGO }, null, options);
            _assets.Add(result.VisualMesh);

            Assert.AreEqual(0, result.PrimitiveColliders.Count);
        }

        [Test]
        public void Combine_ColliderOnlySourceWithDisabledCollider_IsSkipped()
        {
            var colliderOnlyGO = new GameObject("ColliderOnly");
            _spawned.Add(colliderOnlyGO);
            colliderOnlyGO.AddComponent<BoxCollider>().enabled = false;

            Material mat = NewMaterial("A");
            _assets.Add(mat);
            GameObject cube = CreateCubeSource("Cube", mat);
            Object.DestroyImmediate(cube.GetComponent<BoxCollider>());

            var options = DefaultOptions;
            options.IncludeColliders = true;
            var result = MeshCombinerTool.Combine(
                new[] { cube.GetComponent<MeshFilter>() }, new[] { colliderOnlyGO }, null, options);
            _assets.Add(result.VisualMesh);

            Assert.AreEqual(0, result.PrimitiveColliders.Count);
        }

        [Test]
        public void Combine_ColliderOnlySourceWithSolidMeshCollider_MergesIntoCollisionMesh()
        {
            var colliderOnlyGO = new GameObject("ColliderOnly");
            _spawned.Add(colliderOnlyGO);
            var meshCollider = colliderOnlyGO.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = GameObject.CreatePrimitive(PrimitiveType.Cube).GetComponent<MeshFilter>().sharedMesh;

            Material mat = NewMaterial("A");
            _assets.Add(mat);
            GameObject cube = CreateCubeSource("Cube", mat);
            Object.DestroyImmediate(cube.GetComponent<BoxCollider>());

            var options = DefaultOptions;
            options.IncludeColliders = true;
            var result = MeshCombinerTool.Combine(
                new[] { cube.GetComponent<MeshFilter>() }, new[] { colliderOnlyGO }, null, options);
            _assets.Add(result.VisualMesh);
            if (result.CollisionMesh != null) _assets.Add(result.CollisionMesh);

            Assert.IsNotNull(result.CollisionMesh);
            Assert.AreEqual(0, result.PrimitiveColliders.Count);
        }

        [Test]
        public void Combine_ColliderOnlySourceExcludedEditorOnly_IsSkipped()
        {
            var colliderOnlyGO = new GameObject("ColliderOnly");
            _spawned.Add(colliderOnlyGO);
            colliderOnlyGO.AddComponent<BoxCollider>();
            colliderOnlyGO.tag = "EditorOnly";

            Material mat = NewMaterial("A");
            _assets.Add(mat);
            GameObject cube = CreateCubeSource("Cube", mat);
            Object.DestroyImmediate(cube.GetComponent<BoxCollider>());

            var options = DefaultOptions;
            options.IncludeColliders = true;
            options.ExcludeEditorOnly = true;

            var result = MeshCombinerTool.Combine(
                new[] { cube.GetComponent<MeshFilter>() }, new[] { colliderOnlyGO }, null, options);
            _assets.Add(result.VisualMesh);

            Assert.AreEqual(0, result.PrimitiveColliders.Count);
        }

        [Test]
        public void Combine_NullColliderOnlySourceEntry_IsSkipped()
        {
            Material mat = NewMaterial("A");
            _assets.Add(mat);
            GameObject cube = CreateCubeSource("Cube", mat);

            var options = DefaultOptions;
            options.IncludeColliders = true;

            Assert.DoesNotThrow(() =>
            {
                var result = MeshCombinerTool.Combine(
                    new[] { cube.GetComponent<MeshFilter>() }, new GameObject[] { null }, null, options);
                _assets.Add(result.VisualMesh);
            });
        }

        [Test]
        public void Combine_ColliderOnlySourcesNull_DoesNotThrowWhenIncludeCollidersTrue()
        {
            Material mat = NewMaterial("A");
            _assets.Add(mat);
            GameObject cube = CreateCubeSource("Cube", mat);
            Object.DestroyImmediate(cube.GetComponent<BoxCollider>());

            var options = DefaultOptions;
            options.IncludeColliders = true;

            Assert.DoesNotThrow(() =>
            {
                var result = MeshCombinerTool.Combine(new[] { cube.GetComponent<MeshFilter>() }, null, null, options);
                _assets.Add(result.VisualMesh);
            });
        }

        [Test]
        public void Combine_RecalculateNormalsTrue_ProducesNonZeroNormals()
        {
            Material mat = NewMaterial("A");
            _assets.Add(mat);
            GameObject cube = CreateCubeSource("Cube", mat);

            var options = DefaultOptions;
            options.RecalculateNormals = true;
            var result = MeshCombinerTool.Combine(new[] { cube.GetComponent<MeshFilter>() }, null, null, options);
            _assets.Add(result.VisualMesh);

            Assert.Greater(result.VisualMesh.normals.Length, 0);
        }

        [Test]
        public void Combine_RecalculateNormalsFalse_DoesNotRecomputeClearedNormals()
        {
            // Distinguishes "RecalculateNormals=false" from merely "normals happen to be
            // present" (the cube's baked-in normals would pass a weaker "non-zero" check
            // either way): explicitly clear the source mesh's normals, then confirm
            // Combine leaves them cleared instead of silently recalculating anyway.
            var go = new GameObject("NoNormals");
            _spawned.Add(go);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();

            Mesh cubeMesh = GameObject.CreatePrimitive(PrimitiveType.Cube).GetComponent<MeshFilter>().sharedMesh;
            var mesh = new Mesh { vertices = cubeMesh.vertices, triangles = cubeMesh.triangles, normals = new Vector3[0] };
            mf.sharedMesh = mesh;
            _assets.Add(mesh);

            Material mat = NewMaterial("A");
            _assets.Add(mat);
            mr.sharedMaterial = mat;

            var result = MeshCombinerTool.Combine(new[] { mf }, null, null, DefaultOptions);
            _assets.Add(result.VisualMesh);

            Assert.AreEqual(0, result.VisualMesh.normals.Length, "RecalculateNormals=false must not recompute normals that were absent on the source.");
        }

    }
}
