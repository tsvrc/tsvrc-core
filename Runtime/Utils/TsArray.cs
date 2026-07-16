using UdonSharp;

namespace Tsvrc.Utils
{
    /// <summary>
    /// Static helpers for common array operations on types that Udon cannot express generically.
    /// Each method allocates a new array and leaves the original unchanged.
    /// </summary>
    public class TsArray
    {
        /// <summary>
        /// Returns a new array containing all elements of <paramref name="original"/>
        /// followed by all elements of <paramref name="items"/>.
        /// </summary>
        public static string[] Add(string[] original, string[] items)
        {
            int originalLen = original.Length;
            int itemsLen = items.Length;
            string[] result = new string[originalLen + itemsLen];
            for (int i = 0; i < originalLen; i++)
                result[i] = original[i];
            for (int i = 0; i < itemsLen; i++)
                result[originalLen + i] = items[i];
            return result;
        }

        /// <summary>
        /// Returns a new array with every element that appears in <paramref name="items"/> removed
        /// from <paramref name="original"/>. Order is preserved. All matching occurrences are removed.
        /// </summary>
        public static string[] Remove(string[] original, string[] items)
        {
            int originalLen = original.Length;
            int itemsLen = items.Length;
            string[] buffer = new string[originalLen];
            int count = 0;
            for (int i = 0; i < originalLen; i++)
            {
                string current = original[i];
                bool shouldRemove = false;
                for (int j = 0; j < itemsLen; j++)
                    if (current == items[j]) { shouldRemove = true; break; }
                if (!shouldRemove) buffer[count++] = current;
            }
            if (count == originalLen) return buffer;
            string[] result = new string[count];
            System.Array.Copy(buffer, result, count);
            return result;
        }

        /// <summary>Returns true if <paramref name="value"/> exists in <paramref name="array"/>.</summary>
        public static bool Contains(string[] array, string value)
        {
            int len = array.Length;
            for (int i = 0; i < len; i++)
                if (array[i] == value) return true;
            return false;
        }

        /// <summary>
        /// Returns a new array with repeated values collapsed to their first occurrence.
        /// Order is preserved.
        /// </summary>
        public static string[] Dedupe(string[] original)
        {
            int originalLen = original.Length;
            if (originalLen < 2)
            {
                string[] copy = new string[originalLen];
                System.Array.Copy(original, copy, originalLen);
                return copy;
            }

            string[] deduped = new string[originalLen];
            int dedupedCount = 0;
            for (int i = 0; i < originalLen; i++)
            {
                bool isDuplicate = false;
                for (int j = 0; j < dedupedCount; j++)
                {
                    if (deduped[j] == original[i]) { isDuplicate = true; break; }
                }
                if (!isDuplicate)
                    deduped[dedupedCount++] = original[i];
            }

            if (dedupedCount == originalLen) return deduped;

            string[] trimmed = new string[dedupedCount];
            System.Array.Copy(deduped, trimmed, dedupedCount);
            return trimmed;
        }

        /// <summary>
        /// Returns a new array containing all elements of <paramref name="original"/>
        /// followed by all elements of <paramref name="items"/>.
        /// </summary>
        public static UdonSharpBehaviour[] Add(UdonSharpBehaviour[] original, UdonSharpBehaviour[] items)
        {
            int originalLen = original.Length;
            int itemsLen = items.Length;
            UdonSharpBehaviour[] result = new UdonSharpBehaviour[originalLen + itemsLen];
            for (int i = 0; i < originalLen; i++)
                result[i] = original[i];
            for (int i = 0; i < itemsLen; i++)
                result[originalLen + i] = items[i];
            return result;
        }

        /// <summary>
        /// Returns a new array with every element that appears in <paramref name="items"/> removed
        /// from <paramref name="original"/>. Order is preserved. All matching occurrences are removed.
        /// </summary>
        public static UdonSharpBehaviour[] Remove(UdonSharpBehaviour[] original, UdonSharpBehaviour[] items)
        {
            int originalLen = original.Length;
            int itemsLen = items.Length;
            UdonSharpBehaviour[] buffer = new UdonSharpBehaviour[originalLen];
            int count = 0;
            for (int i = 0; i < originalLen; i++)
            {
                UdonSharpBehaviour current = original[i];
                bool shouldRemove = false;
                for (int j = 0; j < itemsLen; j++)
                    if (current == items[j]) { shouldRemove = true; break; }
                if (!shouldRemove) buffer[count++] = current;
            }
            if (count == originalLen) return buffer;
            UdonSharpBehaviour[] result = new UdonSharpBehaviour[count];
            System.Array.Copy(buffer, result, count);
            return result;
        }

        /// <summary>Returns true if <paramref name="value"/> exists in <paramref name="array"/>.</summary>
        public static bool Contains(UdonSharpBehaviour[] array, UdonSharpBehaviour value)
        {
            int len = array.Length;
            for (int i = 0; i < len; i++)
                if (array[i] == value) return true;
            return false;
        }
    }
}
