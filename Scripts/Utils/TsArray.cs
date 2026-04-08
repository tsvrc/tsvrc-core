using UdonSharp;

namespace Tsvrc.Utils
{
    public class TsArray
    {
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

        public static bool Contains(string[] array, string value)
        {
            int len = array.Length;
            for (int i = 0; i < len; i++)
                if (array[i] == value) return true;
            return false;
        }

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

        public static bool Contains(UdonSharpBehaviour[] array, UdonSharpBehaviour value)
        {
            int len = array.Length;
            for (int i = 0; i < len; i++)
                if (array[i] == value) return true;
            return false;
        }
    }
}
