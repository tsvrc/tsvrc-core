using System.Collections.Generic;
using NUnit.Framework;
using Tsvrc.Player;
using UnityEngine;

namespace Tsvrc.Tests.Editor
{
    public abstract class HeadClipGuardTestBase
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
                if (go != null)
                    Object.DestroyImmediate(go);

            _spawned.Clear();
        }

        protected HeadClipGuard CreateGuard()
        {
            var go = new GameObject(nameof(HeadClipGuard));
            _spawned.Add(go);
            return go.AddComponent<HeadClipGuard>();
        }

        protected BoxCollider CreateBoxCollider(string name, Vector3 position, Quaternion rotation, Vector3 scale, Vector3 size, Vector3 center)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = scale;
            var col = go.AddComponent<BoxCollider>();
            col.size = size;
            col.center = center;
            return col;
        }

        protected static void SetField(object target, string name, object value) => PrivateFieldAccess.SetField(target, name, value);
        protected static T GetField<T>(object target, string name) => PrivateFieldAccess.GetField<T>(target, name);
        protected static object Invoke(object target, string method, params object[] args) => PrivateFieldAccess.InvokeInstance(target, method, args);
    }
}
