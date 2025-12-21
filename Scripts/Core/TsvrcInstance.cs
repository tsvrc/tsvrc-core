using UdonSharp;
using VRC.SDKBase;

namespace Tsvrc.Core
{
    public class TsvrcInstance : UdonSharpBehaviour
    {
        public VRCPlayerApi LocalPlayer => Networking.LocalPlayer;
        public VRCPlayerApi MasterPlayer => Networking.Master;
        public VRCPlayerApi ActiveMasterPlayer;
        public VRCPlayerApi[] GetAllPlayers()
        {
            int[] ids = ParsePlayerIds();
            int validCount = 0;
            VRCPlayerApi[] tempPlayers = new VRCPlayerApi[ids.Length];

            for (int i = 0; i < ids.Length; i++)
            {
                VRCPlayerApi player = VRCPlayerApi.GetPlayerById(ids[i]);
                if (player != null)
                {
                    tempPlayers[validCount] = player;
                    validCount++;
                }
            }

            VRCPlayerApi[] players = new VRCPlayerApi[validCount];
            for (int i = 0; i < validCount; i++)
            {
                players[i] = tempPlayers[i];
            }
            return players;
        }
        [UdonSynced] private string _allPlayerIdsString;

        public void ConstructInstance()
        {
            ActiveMasterPlayer = Networking.Master;
            VRCPlayerApi.GetPlayerById(ActiveMasterPlayer.playerId);
        }

        public void SetMasterPlayer(VRCPlayerApi newMaster)
        {
            ActiveMasterPlayer = newMaster;
        }

        private int[] ParsePlayerIds()
        {
            if (string.IsNullOrEmpty(_allPlayerIdsString))
            {
                return new int[0];
            }

            string[] idStrings = _allPlayerIdsString.Split(',');
            int[] ids = new int[idStrings.Length];
            int count = 0;

            for (int i = 0; i < idStrings.Length; i++)
            {
                if (int.TryParse(idStrings[i], out int id))
                {
                    ids[count] = id;
                    count++;
                }
            }

            int[] result = new int[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = ids[i];
            }
            return result;
        }

        private void AddPlayerIdToString(int playerId)
        {
            if (string.IsNullOrEmpty(_allPlayerIdsString))
            {
                _allPlayerIdsString = playerId.ToString();
            }
            else
            {
                _allPlayerIdsString = _allPlayerIdsString + "," + playerId.ToString();
            }
            RequestSerialization();
        }

        private void RemovePlayerIdFromString(int playerId)
        {
            int[] ids = ParsePlayerIds();
            int count = 0;
            int[] newIds = new int[ids.Length];

            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i] != playerId)
                {
                    newIds[count] = ids[i];
                    count++;
                }
            }

            if (count == 0)
            {
                _allPlayerIdsString = "";
            }
            else
            {
                string[] idStrings = new string[count];
                for (int i = 0; i < count; i++)
                {
                    idStrings[i] = newIds[i].ToString();
                }
                _allPlayerIdsString = string.Join(",", idStrings);
            }
            RequestSerialization();
        }

        private bool ContainsPlayerId(int playerId)
        {
            int[] ids = ParsePlayerIds();
            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i] == playerId)
                {
                    return true;
                }
            }
            return false;
        }

#pragma warning disable USH0016
        public override void OnPlayerJoined(VRCPlayerApi player)
#pragma warning restore USH0016
        {
            if (Networking.IsMaster || Networking.LocalPlayer == ActiveMasterPlayer)
            {
                if (!ContainsPlayerId(player.playerId))
                {
                    AddPlayerIdToString(player.playerId);
                }
            }
        }

#pragma warning disable USH0016
        public override void OnPlayerLeft(VRCPlayerApi player)
#pragma warning restore USH0016
        {
            if (player == ActiveMasterPlayer)
            {
                ActiveMasterPlayer = Networking.Master;
            }

            if (Networking.IsMaster || Networking.LocalPlayer == ActiveMasterPlayer)
            {
                RemovePlayerIdFromString(player.playerId);
            }
        }
    }
}