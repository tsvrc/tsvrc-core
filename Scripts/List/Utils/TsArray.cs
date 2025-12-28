namespace Tsvrc.List.Utils
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

        public static int[] Add(int[] originalArray, int[] intsToAdd)
        {
            int[] result = new int[originalArray.Length + intsToAdd.Length];

            for (int i = 0; i < originalArray.Length; i++)
            {
                result[i] = originalArray[i];
            }

            for (int i = 0; i < intsToAdd.Length; i++)
            {
                result[originalArray.Length + i] = intsToAdd[i];
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

        public static int[] Remove(int[] originalArray, int[] intsToRemove)
        {
            int count = 0;

            for (int i = 0; i < originalArray.Length; i++)
            {
                bool shouldRemove = false;
                for (int j = 0; j < intsToRemove.Length; j++)
                {
                    if (originalArray[i] == intsToRemove[j])
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

            int[] result = new int[count];
            int index = 0;

            for (int i = 0; i < originalArray.Length; i++)
            {
                bool shouldRemove = false;
                for (int j = 0; j < intsToRemove.Length; j++)
                {
                    if (originalArray[i] == intsToRemove[j])
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
    }
}