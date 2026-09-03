using Tsvrc.Core;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Tsvrc.Player
{
    /// <summary>
    /// Assigns each player a stable color, keyed off their own numeric VRChat player id (see
    /// <see cref="TsPlayer.GetNumericPlayerId"/>) so every client resolves the same slot. Which
    /// color lands in each slot is decided once by the owner and synced (see <see cref="_order"/>),
    /// so every client's <see cref="GetColor"/> agrees. Call <see cref="Initialize"/> once per
    /// instance start, before <see cref="GetColor"/>. Consumers that need matching colors share
    /// the one instance rather than deriving their own.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class PlayerColorAssigner : TsvrcBehaviour
    {
        protected override bool IsTsvrcInternal => true;

        // 82 colors (VRChat's hard instance player cap), evenly-spaced hue sweep for max
        // distinctness, twice around the wheel at alternating saturation/value.
        [SerializeField]
        private Color[] _palette =
        {
            new Color(0.920f, 0.110f, 0.110f),
            new Color(0.620f, 0.189f, 0.155f),
            new Color(0.920f, 0.229f, 0.110f),
            new Color(0.620f, 0.257f, 0.155f),
            new Color(0.920f, 0.347f, 0.110f),
            new Color(0.620f, 0.325f, 0.155f),
            new Color(0.920f, 0.466f, 0.110f),
            new Color(0.620f, 0.393f, 0.155f),
            new Color(0.920f, 0.584f, 0.110f),
            new Color(0.620f, 0.461f, 0.155f),
            new Color(0.920f, 0.703f, 0.110f),
            new Color(0.620f, 0.529f, 0.155f),
            new Color(0.920f, 0.821f, 0.110f),
            new Color(0.620f, 0.597f, 0.155f),
            new Color(0.900f, 0.920f, 0.110f),
            new Color(0.575f, 0.620f, 0.155f),
            new Color(0.782f, 0.920f, 0.110f),
            new Color(0.507f, 0.620f, 0.155f),
            new Color(0.663f, 0.920f, 0.110f),
            new Color(0.439f, 0.620f, 0.155f),
            new Color(0.545f, 0.920f, 0.110f),
            new Color(0.370f, 0.620f, 0.155f),
            new Color(0.426f, 0.920f, 0.110f),
            new Color(0.302f, 0.620f, 0.155f),
            new Color(0.308f, 0.920f, 0.110f),
            new Color(0.234f, 0.620f, 0.155f),
            new Color(0.189f, 0.920f, 0.110f),
            new Color(0.166f, 0.620f, 0.155f),
            new Color(0.110f, 0.920f, 0.150f),
            new Color(0.155f, 0.620f, 0.212f),
            new Color(0.110f, 0.920f, 0.268f),
            new Color(0.155f, 0.620f, 0.280f),
            new Color(0.110f, 0.920f, 0.387f),
            new Color(0.155f, 0.620f, 0.348f),
            new Color(0.110f, 0.920f, 0.505f),
            new Color(0.155f, 0.620f, 0.416f),
            new Color(0.110f, 0.920f, 0.624f),
            new Color(0.155f, 0.620f, 0.484f),
            new Color(0.110f, 0.920f, 0.742f),
            new Color(0.155f, 0.620f, 0.552f),
            new Color(0.110f, 0.920f, 0.861f),
            new Color(0.155f, 0.620f, 0.620f),
            new Color(0.110f, 0.861f, 0.920f),
            new Color(0.155f, 0.552f, 0.620f),
            new Color(0.110f, 0.742f, 0.920f),
            new Color(0.155f, 0.484f, 0.620f),
            new Color(0.110f, 0.624f, 0.920f),
            new Color(0.155f, 0.416f, 0.620f),
            new Color(0.110f, 0.505f, 0.920f),
            new Color(0.155f, 0.348f, 0.620f),
            new Color(0.110f, 0.387f, 0.920f),
            new Color(0.155f, 0.280f, 0.620f),
            new Color(0.110f, 0.268f, 0.920f),
            new Color(0.155f, 0.212f, 0.620f),
            new Color(0.110f, 0.150f, 0.920f),
            new Color(0.166f, 0.155f, 0.620f),
            new Color(0.189f, 0.110f, 0.920f),
            new Color(0.234f, 0.155f, 0.620f),
            new Color(0.308f, 0.110f, 0.920f),
            new Color(0.302f, 0.155f, 0.620f),
            new Color(0.426f, 0.110f, 0.920f),
            new Color(0.370f, 0.155f, 0.620f),
            new Color(0.545f, 0.110f, 0.920f),
            new Color(0.439f, 0.155f, 0.620f),
            new Color(0.663f, 0.110f, 0.920f),
            new Color(0.507f, 0.155f, 0.620f),
            new Color(0.782f, 0.110f, 0.920f),
            new Color(0.575f, 0.155f, 0.620f),
            new Color(0.900f, 0.110f, 0.920f),
            new Color(0.620f, 0.155f, 0.597f),
            new Color(0.920f, 0.110f, 0.821f),
            new Color(0.620f, 0.155f, 0.529f),
            new Color(0.920f, 0.110f, 0.703f),
            new Color(0.620f, 0.155f, 0.461f),
            new Color(0.920f, 0.110f, 0.584f),
            new Color(0.620f, 0.155f, 0.393f),
            new Color(0.920f, 0.110f, 0.466f),
            new Color(0.620f, 0.155f, 0.325f),
            new Color(0.920f, 0.110f, 0.347f),
            new Color(0.620f, 0.155f, 0.257f),
            new Color(0.920f, 0.110f, 0.229f),
            new Color(0.620f, 0.155f, 0.189f),
        };

        // Synced permutation of palette indices: _order[slot] is which palette entry that slot
        // resolves to. Only the owner ever shuffles it (see Initialize); everyone else just
        // reads whatever they receive, so every client's GetColor agrees. Defaults to identity
        // (0, 1, 2, ...) so GetColor is sane even before Initialize or the first sync arrives.
        [UdonSynced] private byte[] _order;

        /// <summary>
        /// Shuffles the palette order for this session and syncs it. Call once per instance
        /// start, before <see cref="GetColor"/>. Only the owner actually reshuffles and
        /// broadcasts; a non-owner's call just ensures a sane default is in place until the
        /// owner's real order arrives.
        /// </summary>
        public void Initialize()
        {
            if (_order == null || _order.Length != _palette.Length)
            {
                _order = new byte[_palette.Length];
                for (int i = 0; i < _order.Length; i++) _order[i] = (byte)i;
            }

            if (!Networking.IsOwner(gameObject)) return;

            for (int i = _order.Length - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                byte swap = _order[i];
                _order[i] = _order[j];
                _order[j] = swap;
            }
            RequestSerialization();
        }

        /// <summary>
        /// Returns the color for <paramref name="playerId"/> (<see cref="TsPlayer.GetPlayerID"/>
        /// format), derived from their numeric player id modulo the palette size, indirected
        /// through the synced <see cref="_order"/>.
        /// </summary>
        public Color GetColor(string playerId)
        {
            if (_palette == null || _palette.Length == 0) return Color.white;
            int slot = TsPlayer.GetNumericPlayerId(playerId) % _palette.Length;
            if (_order == null || _order.Length != _palette.Length) return _palette[slot];
            return _palette[_order[slot]];
        }
    }
}
