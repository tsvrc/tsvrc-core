using Tsvrc.Process;
using UnityEngine;

namespace Tsvrc.Network
{
    /// <summary>
    /// Provides chunk-size constants and pure chunking math for string data.
    /// No network, no process state — only splitting and validation logic.
    /// </summary>
    public class DataChunker : ReadyCheckProcess
    {
        // BroadcastDataChunkReceived carries four parameters whose total encoded size must stay
        // under VRChat's hard 16 KB (16,384 byte) per-event limit (docs: new byte[16*1024] =
        // "maximum allowed size"). All four are counted together:
        //
        //   dataChunk (string)  : CHUNK_SIZE × bytes_per_char (UTF-8)
        //   chunkIndex (int)    : 4 bytes
        //   totalChunks (int)   : 4 bytes
        //   playerIds (string[]): sum(UTF-8 bytes per ID) + N × 4 bytes (length-field overhead)
        //                         (docs: new string[2]{"test","foobar"} = 4+6+8 = 18 bytes)
        //
        // Worst case bytes per C# char: 3 bytes (BMP characters U+0800–U+FFFF, e.g. CJK).
        // Surrogate pairs (emoji, U+10000+) are 2 C# chars → 4 UTF-8 bytes = 2 bytes/char,
        // strictly less than CJK, so CJK is the binding worst case — NOT emoji.
        //
        // playerIds format: "displayName#playerId". VRCPlayerApi.playerId is the instance-local
        // runtime player ID (1–80 range for a standard 80-player instance = 2 decimal digits).
        // Max display name: 32 chars. Worst-case CJK ID: 32×3 + 1 + 2 + 4 = 103 bytes/entry.
        //
        // Safety check for CHUNK_SIZE=2500 with 80 CJK-named players and CJK data:
        //   2500 × 3 + 8 + 80 × 103 = 7,500 + 8 + 8,240 = 15,748 bytes ≤ 16,384 ✓
        //
        // CHUNK_SIZE=3000 overflows starting at 72 CJK-named players with CJK data:
        //   3000 × 3 + 8 + 72 × 103 = 9,000 + 8 + 7,416 = 16,424 bytes → event silently
        //   dropped → no ACK → transfer stalls indefinitely.
        protected const int CHUNK_SIZE = 2500;
        protected const int MAX_MESSAGE_SIZE = 500000;

        /// <summary>
        /// Checks if a message is valid for transfer.
        /// </summary>
        protected bool ValidateMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                Debug.LogWarning("[TsvrcDataSender] Cannot send empty message");
                return false;
            }

            if (message.Length > MAX_MESSAGE_SIZE)
            {
                Debug.LogError($"[TsvrcDataSender] Message too large: {message.Length} chars (max {MAX_MESSAGE_SIZE})");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Creates data chunks from the full data string.
        /// </summary>
        protected string[] CreateDataChunks(string data)
        {
            var chunksCount = CalculateTotalChunks(data.Length);
            var chunks = new string[chunksCount];

            for (int i = 0; i < chunksCount; i++)
                chunks[i] = ExtractChunk(data, i);

            return chunks;
        }

        /// <summary>
        /// Calculates the total number of chunks needed for the data length.
        /// </summary>
        protected int CalculateTotalChunks(int dataLength)
        {
            return (dataLength + CHUNK_SIZE - 1) / CHUNK_SIZE;
        }

        /// <summary>
        /// Extracts a specific chunk from the data string.
        /// </summary>
        protected string ExtractChunk(string data, int chunkIndex)
        {
            int startIndex = chunkIndex * CHUNK_SIZE;
            int length = System.Math.Min(CHUNK_SIZE, data.Length - startIndex);
            return data.Substring(startIndex, length);
        }
    }
}
