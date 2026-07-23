using UnityEngine;
using VRC.SDK3.ClientSim.Persistence;

namespace Tsvrc.Testing.Framework
{
    /// <summary>
    /// ClientSimPlayer.SetupPlayerPersistence (active when VRC_ENABLE_PLAYER_PERSISTENCE
    /// is defined) instantiates its PlayerDataPrefab as an unparented root object, so
    /// ClientSimMain.RemoveInstance() never destroys it - a genuine leak in ClientSim
    /// itself, hidden from the Hierarchy window via HideFlags.HideInHierarchy. Left alive,
    /// each leaked ClientSimPlayerObjectStorage keeps polling and writing to the same
    /// fixed PlayerObject_&lt;playerId&gt;_&lt;sceneName&gt;.json path forever, so
    /// back-to-back tests accumulate more and more concurrent writers all colliding on
    /// that path - a Windows "Sharing violation" that never clears once several leaked
    /// writers are racing it. Explicitly destroying any leaked instances after every test
    /// keeps each test starting from zero.
    /// </summary>
    public sealed class ClientSimPersistenceLeakFixup : IPlayModeEnvironmentFixup
    {
        public void OnBeforeAnyTests()
        {
        }

        public void OnUnityTearDown()
        {
            // Resources.FindObjectsOfTypeAll returns every object of this type loaded in memory,
            // including project Prefab Assets themselves, not just scene-instantiated leaks -
            // scene.IsValid() is false for a loaded-but-not-instantiated asset, so this guard
            // keeps this fixup from ever touching a Prefab Asset and scoped to genuine scene leaks.
            foreach (var storage in Resources.FindObjectsOfTypeAll<ClientSimPlayerObjectStorage>())
                if (storage != null && storage.gameObject.scene.IsValid())
                    Object.DestroyImmediate(storage.gameObject, true);
        }
    }
}
