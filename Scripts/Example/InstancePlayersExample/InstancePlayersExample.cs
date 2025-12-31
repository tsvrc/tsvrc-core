using Tsvrc.TsNetworking;
using Tsvrc.TsNetworking.Utils;
using UnityEngine;
using VRC.SDKBase;

namespace Tsvrc.Example
{
    public class InstancePlayersExample : TsvrcAutoPlayerListTracker
    {
        [SerializeField] private Transform ContentParent;
        [SerializeField] private GameObject InstancePlayersExampleSlot;

        protected override void Start()
        {
            base.Start();
            if (Networking.IsMaster)
            {
                Debug.Log("[InstancePlayersExample] Master starting player tracking.");
                StartProcess();

                VRCPlayerApi[] allPlayers = new VRCPlayerApi[1];
                allPlayers[0] = Networking.LocalPlayer;
                AddTrackedPlayers(TsPlayerUtils.ToPlayerIDs(allPlayers));
            }
        }

        protected override void OnTrackedPlayersSynced(VRCPlayerApi[] players)
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

        protected override void OnTrackedPlayersAdded(VRCPlayerApi[] players)
        {
            Debug.Log($"[InstancePlayersExample] Players added: {players.Length}");
            foreach (var player in players)
            {
                var slotObj = Instantiate(InstancePlayersExampleSlot, ContentParent);
                var slot = slotObj.GetComponent<InstancePlayersExampleSlot>();
                slot.SetPlayerName(player.displayName);
                slotObj.SetActive(true);
            }
        }

        protected override void OnTrackedPlayersRemoved(VRCPlayerApi[] players)
        {
            Debug.Log($"[InstancePlayersExample] Players removed: {players.Length}");
            foreach (var player in players)
            {
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
}