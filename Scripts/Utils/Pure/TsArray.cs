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
            int count = 0;
            bool shouldRemove;
            string current;
            for (int i = 0; i < originalLen; i++)
            {
                shouldRemove = false;
                current = original[i];
                for (int j = 0; j < itemsLen; j++)
                    if (current == items[j]) { shouldRemove = true; break; }
                if (!shouldRemove) count++;
            }
            string[] result = new string[count];
            int index = 0;
            for (int i = 0; i < originalLen; i++)
            {
                shouldRemove = false;
                current = original[i];
                for (int j = 0; j < itemsLen; j++)
                    if (current == items[j]) { shouldRemove = true; break; }
                if (!shouldRemove) result[index++] = current;
            }
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
            int count = 0;
            bool shouldRemove;
            UdonSharpBehaviour current;
            for (int i = 0; i < originalLen; i++)
            {
                shouldRemove = false;
                current = original[i];
                for (int j = 0; j < itemsLen; j++)
                    if (current == items[j]) { shouldRemove = true; break; }
                if (!shouldRemove) count++;
            }
            UdonSharpBehaviour[] result = new UdonSharpBehaviour[count];
            int index = 0;
            for (int i = 0; i < originalLen; i++)
            {
                shouldRemove = false;
                current = original[i];
                for (int j = 0; j < itemsLen; j++)
                    if (current == items[j]) { shouldRemove = true; break; }
                if (!shouldRemove) result[index++] = current;
            }
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
