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
            foreach (var storage in Resources.FindObjectsOfTypeAll<ClientSimPlayerObjectStorage>())
                if (storage != null)
                    Object.DestroyImmediate(storage.gameObject);
        }

        public void OnAfterAllTests(bool allPassed)
        {
        }
    }
}
