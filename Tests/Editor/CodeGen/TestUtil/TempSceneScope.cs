using System;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Tsvrc.Tests.Editor
{
    // Creates a brand-new, unsaved Editor scene so CodeGen module tests never read or
    // mutate the developer's actual open scene/config. Unity has no invisible sandbox
    // scene for Edit Mode tests, so this replaces the active scene outright - save your
    // work before running the CodeGen suite interactively. See CODEGEN_TESTING_PLAN.md
    // Part 2.2 for why this (not a mock root) is the isolation strategy.
    internal sealed class TempSceneScope : IDisposable
    {
        private readonly List<GameObject> _tracked = new List<GameObject>();

        internal Scene Scene { get; }

        internal TempSceneScope()
        {
            Scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        internal GameObject CreateGameObject(string name)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, Scene);
            _tracked.Add(go);
            return go;
        }

        public void Dispose()
        {
            foreach (var go in _tracked)
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _tracked.Clear();
        }
    }
}
