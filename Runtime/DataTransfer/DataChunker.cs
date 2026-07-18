using Tsvrc.Tracking;

namespace Tsvrc.DataTransfer
{
    /// <summary>
    /// Provides chunk-size constants and string chunking utilities.
    /// This class has no network or process state. It only handles splitting and validating string data.
    /// </summary>
    public class DataChunker : ReadyCheckProcess
    {
        // CHUNK_SIZE controls how many characters go into each BroadcastDataChunkReceived call.
        // That event carries four parameters and their combined encoded size must stay under
        // VRChat's hard 16 KB (16,384 byte) per-event limit (docs: new byte[16*1024] is the maximum).
        //
        // Parameter sizes:
        //   dataChunk (string)   : CHUNK_SIZE × bytes per char (UTF-8)
        //   chunkIndex (int)     : 4 bytes
        //   totalChunks (int)    : 4 bytes
        //   playerIds (string[]) : UTF-8 bytes per ID + 4 bytes overhead per entry
        //                          (e.g. new string[2]{"test","foobar"} = 4+6+8 = 18 bytes)
        //
        // UTF-8 encodes BMP characters in the U+0800 to U+FFFF range (e.g. CJK) at 3 bytes per char.
        // Emoji and other characters above U+FFFF use 2 C# chars but only 4 UTF-8 bytes total,
        // which is 2 bytes per char. CJK is therefore the real worst case, not emoji.
        //
        // Player IDs use the format "displayName#playerId". The numeric part is assigned in
        // monotonically increasing order and never reused when a player leaves. In a busy session
        // with player turnover, IDs can grow past the concurrent player cap, so a 3-digit bound
        // (up to 999 total distinct joins) is a safe and realistic estimate.
        // A 32-char CJK display name with a 3-digit ID gives 32×3 + 1 + 3 + 4 = 104 bytes per entry.
        //
        // Safety check for CHUNK_SIZE=2500 with 80 CJK-named players and CJK data:
        //   2500 × 3 + 8 + 80 × 104 = 7,500 + 8 + 8,320 = 15,828 bytes, within the 16,384 limit. ✓
        //
        // At CHUNK_SIZE=3000 the limit breaks at 71 CJK-named players:
        //   3000 × 3 + 8 + 71 × 104 = 9,000 + 8 + 7,384 = 16,392 bytes, over the limit.
        //   An oversized event is silently dropped, the receiver never ACKs, and the transfer stalls.
        protected const int CHUNK_SIZE = 2500;
        protected const int MAX_MESSAGE_SIZE = 500000;

        /// <summary>
        /// Returns true if the message is non-empty and within the allowed size limit.
        /// Logs a warning for empty messages and an error if the size limit is exceeded.
        /// </summary>
        /// <param name="message">The message to validate.</param>
        /// <returns>True if the message is valid for transfer, false otherwise.</returns>
        protected bool ValidateMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                LogWarning("Cannot send empty message");
                return false;
            }

            if (message.Length > MAX_MESSAGE_SIZE)
            {
                LogError($"Message too large: {message.Length} chars (max {MAX_MESSAGE_SIZE})");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Splits the data string into sequential chunks of at most <see cref="CHUNK_SIZE"/> characters each.
        /// </summary>
        /// <param name="data">The full string to split.</param>
        /// <returns>An ordered array of chunk strings.</returns>
        protected string[] CreateDataChunks(string data)
        {
            var chunksCount = CalculateTotalChunks(data.Length);
            var chunks = new string[chunksCount];

            for (int i = 0; i < chunksCount; i++)
                chunks[i] = ExtractChunk(data, i);

            return chunks;
        }

        /// <summary>
        /// Returns the number of chunks needed to transmit a string of the given character length.
        /// </summary>
        /// <param name="dataLength">The character length of the full string.</param>
        /// <returns>The total number of chunks.</returns>
        protected int CalculateTotalChunks(int dataLength)
        {
            return (dataLength + CHUNK_SIZE - 1) / CHUNK_SIZE;
        }

        /// <summary>
        /// Returns the substring for the given zero-based chunk index.
        /// The last chunk may be shorter than <see cref="CHUNK_SIZE"/> if the data does not divide evenly.
        /// </summary>
        /// <param name="data">The full string to extract from.</param>
        /// <param name="chunkIndex">The zero-based index of the chunk to extract.</param>
        /// <returns>The chunk substring.</returns>
        protected string ExtractChunk(string data, int chunkIndex)
        {
            int startIndex = chunkIndex * CHUNK_SIZE;
            int length = System.Math.Min(CHUNK_SIZE, data.Length - startIndex);
            return data.Substring(startIndex, length);
        }
    }
}
