namespace Tsvrc.Utils
{
    public class TsArray
    {
        public static string[] Add(string[] originalArray, string[] stringsToAdd)
        {
            string[] result = new string[originalArray.Length + stringsToAdd.Length];

            for (int i = 0; i < originalArray.Length; i++)
            {
                result[i] = originalArray[i];
            }

            for (int i = 0; i < stringsToAdd.Length; i++)
            {
                result[originalArray.Length + i] = stringsToAdd[i];
            }

            return result;
        }

        public static string[] Remove(string[] originalArray, string[] stringsToRemove)
        {
            int count = 0;

            for (int i = 0; i < originalArray.Length; i++)
            {
                bool shouldRemove = false;
                for (int j = 0; j < stringsToRemove.Length; j++)
                {
                    if (originalArray[i] == stringsToRemove[j])
                    {
                        shouldRemove = true;
                        break;
                    }
                }
                if (!shouldRemove)
                {
                    count++;
                }
            }

            string[] result = new string[count];
            int index = 0;

            for (int i = 0; i < originalArray.Length; i++)
            {
                bool shouldRemove = false;
                for (int j = 0; j < stringsToRemove.Length; j++)
                {
                    if (originalArray[i] == stringsToRemove[j])
                    {
                        shouldRemove = true;
                        break;
                    }
                }
                if (!shouldRemove)
                {
                    result[index++] = originalArray[i];
                }
            }

            return result;
        }

        public static bool Contains(string[] array, string value)
        {
            for (int i = 0; i < array.Length; i++)
            {
                if (array[i] == value)
                {
                    return true;
                }
            }
            return false;
        }
    }
}