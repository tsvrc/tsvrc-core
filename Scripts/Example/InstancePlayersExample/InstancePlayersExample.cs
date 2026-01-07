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

        #region Unity Lifecycle

        protected override void Start()
        {
            base.Start();

            if (Networking.IsMaster)
            {
                var playerId = TsPlayerUtils.GetPlayerID(Networking.LocalPlayer);
                var players = TsPlayerUtils.ToArray(playerId);
                StartProcessFromTracker(players);
            }
        }

        #endregion

        #region TsvrcAutoPlayerListTracker Callbacks

        protected override void HandleTrackedPlayersDeserialization(string[] playerIds)
        {
            for (int i = ContentParent.childCount - 1; i >= 0; i--)
            {
                Destroy(ContentParent.GetChild(i).gameObject);
            }

            foreach (var playerId in playerIds)
            {
                var slotObj = Instantiate(InstancePlayersExampleSlot, ContentParent);
                var slot = slotObj.GetComponent<InstancePlayersExampleSlot>();
                var player = TsPlayerUtils.FindPlayerByID(playerId);
                slot.SetPlayerName(player.displayName);
                slotObj.SetActive(true);
            }
        }

        protected override void OnProcessStartedAsTrackedPlayer(string[] playerIds)
        {
            foreach (var playerId in playerIds)
            {
                var slotObj = Instantiate(InstancePlayersExampleSlot, ContentParent);
                var slot = slotObj.GetComponent<InstancePlayersExampleSlot>();
                var player = TsPlayerUtils.FindPlayerByID(playerId);
                slot.SetPlayerName(player.displayName);
                slotObj.SetActive(true);
            }
        }

        protected override void OnPlayersAddedAsTrackedPlayer(string[] playerIds)
        {
            foreach (var playerId in playerIds)
            {
                var slotObj = Instantiate(InstancePlayersExampleSlot, ContentParent);
                var slot = slotObj.GetComponent<InstancePlayersExampleSlot>();
                var player = TsPlayerUtils.FindPlayerByID(playerId);
                slot.SetPlayerName(player.displayName);
                slotObj.SetActive(true);
            }
        }

        protected override void OnPlayersRemovedAsTrackedPlayer(string[] playerIds)
        {
            foreach (var playerId in playerIds)
            {
                for (int i = ContentParent.childCount - 1; i >= 0; i--)
                {
                    var child = ContentParent.GetChild(i);
                    var slot = child.GetComponent<InstancePlayersExampleSlot>();
                    var player = TsPlayerUtils.FindPlayerByID(playerId);
                    if (slot != null && slot.GetPlayerName() == player.displayName)
                    {
                        Destroy(child.gameObject);
                        break;
                    }
                }
            }
        }

        #endregion
    }
}