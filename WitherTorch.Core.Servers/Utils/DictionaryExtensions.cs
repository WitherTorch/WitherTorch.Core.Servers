using System.Collections.Generic;

namespace WitherTorch.Core.Servers.Utils
{
    internal static class DictionaryExtensions
    {
        public static TKey[] ToKeyArray<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> dict)
        {
            int count = dict.Count;
            TKey[] result = new TKey[count];
            var enumerator = dict.Keys.GetEnumerator();
            for (int i = 0; i < count && enumerator.MoveNext(); i++)
            {
                result[i] = enumerator.Current;
            }
            return result;
        }
    }
}
