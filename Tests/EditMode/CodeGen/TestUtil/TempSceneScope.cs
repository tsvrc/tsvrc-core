using System;
using System.Collections.Generic;
using Tsvrc.Editor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Tsvrc.Tests.EditMode
{
    // Creates a brand-new, unsaved Editor scene so CodeGen module tests never read or
    // mutate the developer's actual open scene or config. Unity has no invisible sandbox
    // scene for Edit Mode tests, so this replaces the active scene outright. Save your
    // work before running the CodeGen suite interactively.
    //
    // Also resets TsPaths to its defaults on Dispose, unconditionally. TsPaths is the one
    // other piece of ambient, test-mutable state CodeGen tests touch, see CompiledRootFixture
    // and TsGeneratorTestHarness. Resetting it here, in the one scope type nearly every CodeGen
    // test already uses, means a test that redirects a path can never leak that redirect into
    // the next test, even if it forgets to restore it itself.
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
            TsPaths.ResetToDefaults();
        }
    }
}
