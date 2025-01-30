using System;
using System.Collections.Generic;

namespace WitherTorch.Core.Servers.Utils
{
    internal static class DictionaryExtensions
    {
        public static TKey[] ToKeyArray<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> dict)
        {
            int count = dict.Count;
            if (count <= 0)
                return Array.Empty<TKey>();
            TKey[] result = new TKey[count];
            var enumerator = dict.Keys.GetEnumerator();
            for (int i = 0; i < count && enumerator.MoveNext(); i++)
            {
                result[i] = enumerator.Current;
            }
            return result;
        }

        public static TKey[] ToKeyArray<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> dict, IComparer<TKey> comparer)
        {
            TKey[] result = ToKeyArray(dict);
            if (comparer is null || result.Length <= 0)
                return result;
            Array.Sort(result, comparer);
            return result;
        }
    }
}
