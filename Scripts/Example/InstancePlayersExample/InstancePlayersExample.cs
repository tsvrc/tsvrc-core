using Tsvrc.TsNetworking;
using UnityEngine;
using VRC.SDKBase;

namespace Tsvrc.Example
{
    public class InstancePlayersExample : TsvrcAutoPlayerListTracker
    {
        [SerializeField] private Transform ContentParent;
        [SerializeField] private GameObject InstancePlayersExampleSlot;

        private void Start()
        {
            if (Networking.IsMaster)
            {
                Debug.Log("[InstancePlayersExample] Master starting player tracking.");
                StartTracking();

                // Add all existing players to the tracker
                VRCPlayerApi[] allPlayers = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
                VRCPlayerApi.GetPlayers(allPlayers);
                AddTrackedPlayers(allPlayers);
            }
        }

        protected override void OnTrackerSynced(VRCPlayerApi[] players)
        {
            Debug.Log($"[InstancePlayersExample] Synced player list received. Count: {players.Length}");
            // Clear existing slots
            for (int i = ContentParent.childCount - 1; i >= 0; i--)
            {
                Destroy(ContentParent.GetChild(i).gameObject);
            }

            // Create slots for all tracked players
            foreach (var player in players)
            {
                var slotObj = Instantiate(InstancePlayersExampleSlot, ContentParent);
                var slot = slotObj.GetComponent<InstancePlayersExampleSlot>();
                slot.SetPlayerName(player.displayName);
                slotObj.SetActive(true);
            }
        }

        protected override void OnTrackedPlayerAdded(VRCPlayerApi player)
        {
            Debug.Log($"[InstancePlayersExample] Player added: {player.displayName}");
            var slotObj = Instantiate(InstancePlayersExampleSlot, ContentParent);
            var slot = slotObj.GetComponent<InstancePlayersExampleSlot>();
            slot.SetPlayerName(player.displayName);
            slotObj.SetActive(true);
        }

        protected override void OnTrackedPlayerRemoved(VRCPlayerApi player)
        {
            Debug.Log($"[InstancePlayersExample] Player removed: {player.displayName}");
            // Find and remove the slot for this player
            for (int i = ContentParent.childCount - 1; i >= 0; i--)
            {
                var child = ContentParent.GetChild(i);
                var slot = child.GetComponent<InstancePlayersExampleSlot>();
                if (slot != null && slot.GetPlayerName() == player.displayName)
                {
                    Destroy(child.gameObject);
                    break;
                }
            }
        }
    }
}