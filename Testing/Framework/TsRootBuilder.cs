using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Tsvrc.Testing.Framework
{
    /// <summary>
    /// Builds a project's generated composition root (TsGenerated and friends) from code
    /// instead of loading a saved scene. Every generator output follows the same shape
    /// regardless of project: a root type with a handful of global/pool/construct/
    /// instance fields, and up to six generated bootstrap methods
    /// (<c>_TsLogStart</c>/<c>_TsMemoryStart</c>/<c>_TsGlobalStart</c>/<c>_TsPoolStart</c>/
    /// <c>_TsConstructStart</c>/<c>_TsInstanceStart</c>) that a real client only runs because
    /// Unity dispatches <c>Start()</c> on a scene-loaded, Udon-compiled instance. Building the
    /// same graph with <c>AddComponent</c> instead sidesteps Udon's VM entirely - these become
    /// plain C# objects, so there's no backing UdonBehaviour and no VM dispatch to gate
    /// anything, the same reason every existing tsvrc PlayMode test (see
    /// DataTransfererPlayModeTests, ProcessOwnershipHandoverPlayModeTests) already composes its
    /// subjects with AddComponent rather than loading a scene.
    ///
    /// Not constrained to a Tsvrc.Runtime root type on purpose - this assembly has no
    /// dependency on Tsvrc.Runtime (see the README's opt-in contract), so the six bootstrap
    /// methods are invoked by name via reflection rather than through a shared base type.
    ///
    /// Usage: call <see cref="With{T}"/>/<see cref="WithNew{T}"/> for every root field a test's
    /// scenario actually cares about (wiring each returned instance's own dependencies first),
    /// then <see cref="Build"/>. Any global/pool/construct field the test never touches is
    /// auto-filled with a bare stand-in just before the bootstrap methods run, so a generated
    /// stage that unconditionally iterates every field of its module (e.g. `_TsGlobalStart`
    /// calling `TsConstruct` on every registered global) never NREs on a field the test
    /// didn't care about.
    /// </summary>
    public sealed class TsRootBuilder<TRoot> where TRoot : Component
    {
        private static readonly string[] BootstrapMethodOrder =
        {
            "_TsLogStart", "_TsMemoryStart", "_TsGlobalStart",
            "_TsPoolStart", "_TsConstructStart", "_TsInstanceStart",
        };

        private readonly List<GameObject> _spawned = new List<GameObject>();

        public TRoot Root { get; }

        public TsRootBuilder()
        {
            var go = new GameObject(typeof(TRoot).Name);
            _spawned.Add(go);
            Root = go.AddComponent<TRoot>();
        }

        /// <summary>Assigns <paramref name="instance"/> directly into <paramref name="fieldName"/> on
        /// the root (public or private - see PrivateFieldAccess).</summary>
        public TsRootBuilder<TRoot> With<T>(string fieldName, T instance)
        {
            PrivateFieldAccess.SetField(Root, fieldName, instance);
            return this;
        }

        /// <summary>Creates a fresh <typeparamref name="T"/> via AddComponent, assigns it into
        /// <paramref name="fieldName"/> on the root, and returns the instance so its own fields
        /// can be configured before <see cref="Build"/> runs.</summary>
        public T WithNew<T>(string fieldName) where T : Component
        {
            var go = new GameObject(fieldName);
            _spawned.Add(go);
            var instance = go.AddComponent<T>();
            With(fieldName, instance);
            return instance;
        }

        /// <summary>Auto-fills any still-null Component-typed field on the root with a bare
        /// AddComponent stand-in, then invokes every generated bootstrap method present on the
        /// root's type, in generator order.</summary>
        public TRoot Build()
        {
            AutoFillUnsetComponentFields();

            foreach (var methodName in BootstrapMethodOrder)
            {
                var method = Root.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
                if (method != null) method.Invoke(Root, null);
            }

            return Root;
        }

        /// <summary>Destroys every GameObject this builder created (the root and every
        /// <see cref="WithNew{T}"/>/auto-filled stand-in).</summary>
        public void Teardown()
        {
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go, true);
            _spawned.Clear();
        }

        private void AutoFillUnsetComponentFields()
        {
            for (var t = Root.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public |
                                                   BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (!typeof(Component).IsAssignableFrom(field.FieldType)) continue;
                    if (field.FieldType.IsAbstract || field.FieldType.IsInterface) continue;
                    if (field.GetValue(Root) != null) continue;

                    var go = new GameObject(field.FieldType.Name);
                    _spawned.Add(go);
                    field.SetValue(Root, go.AddComponent(field.FieldType));
                }
            }
        }
    }
}
