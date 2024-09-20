namespace WitherTorch.Core.Servers.Utils
{
    internal static class ArrayExtensions
    {
        public static bool Contains(this string[] array, string key)
        {
            for (int i = 0, count = array.Length; i < count; i++)
            {
                if (array[i].Equals(key))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
