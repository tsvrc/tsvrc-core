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

        protected override void OnTrackedPlayersUpdate(VRCPlayerApi[] players)
        {
            Debug.Log("[InstancePlayersExample] Updating tracked player list display.");
            // Clear existing slots
            for (int i = ContentParent.childCount - 1; i >= 0; i--)
            {
                Destroy(ContentParent.GetChild(i).gameObject);
            }

            // Create new slots for tracked players
            foreach (var player in players)
            {
                var slotObj = Instantiate(InstancePlayersExampleSlot, ContentParent);
                var slot = slotObj.GetComponent<InstancePlayersExampleSlot>();
                slot.SetPlayerName(player.displayName);
                slotObj.SetActive(true);
            }
        }
    }
}