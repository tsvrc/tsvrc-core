using TMPro;
using UdonSharp;
using UnityEngine;

namespace Tsvrc.Example
{
    public class InstancePlayersExampleSlot : UdonSharpBehaviour
    {
        [SerializeField] private TextMeshProUGUI _playerName;

        public void SetPlayerName(string playerName)
        {
            _playerName.text = playerName;
        }
    }
}