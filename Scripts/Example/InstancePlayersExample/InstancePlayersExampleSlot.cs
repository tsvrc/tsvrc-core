using TMPro;
using UdonSharp;
using UnityEngine;

namespace Tsvrc.Example
{
    public class InstancePlayersExampleSlot : UdonSharpBehaviour
    {
        [SerializeField] private TextMeshProUGUI _playerName;
        private string playerName;

        public void SetPlayerName(string playerName)
        {
            this.playerName = playerName;
            _playerName.text = playerName;
        }

        public string GetPlayerName()
        {
            return playerName;
        }
    }
}