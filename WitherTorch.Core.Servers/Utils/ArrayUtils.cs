using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WitherTorch.Core.Servers.Utils
{
    internal static class ArrayUtils
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
