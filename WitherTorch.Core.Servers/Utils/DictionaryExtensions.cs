using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

#if NET8_0_OR_GREATER
using System.Collections.Frozen;
#endif

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IReadOnlyDictionary<TKey, TValue> AsReadOnlyDictionary<TKey, TValue>(this IDictionary<TKey, TValue> _this) where TKey : notnull
        {
#if NET8_0_OR_GREATER
            return _this.ToFrozenDictionary();
#else
            if (_this is IReadOnlyDictionary<TKey, TValue> readonlyDict)
                return readonlyDict;
            return new ReadOnlyDictionaryAdapter<TKey, TValue>(_this);
#endif
        }

#if !NET8_0_OR_GREATER
        private sealed class ReadOnlyDictionaryAdapter<TKey, TValue> : IReadOnlyDictionary<TKey, TValue>
        {
            private readonly IDictionary<TKey, TValue> _dictionary;

            public ReadOnlyDictionaryAdapter(IDictionary<TKey, TValue> dictionary)
            {
                _dictionary = dictionary;
            }

            public TValue this[TKey key] => _dictionary[key];

            public IEnumerable<TKey> Keys => _dictionary.Keys;

            public IEnumerable<TValue> Values => _dictionary.Values;

            public int Count => _dictionary.Count;

            public bool ContainsKey(TKey key) => _dictionary.ContainsKey(key);

            public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _dictionary.GetEnumerator();

            public bool TryGetValue(TKey key, out TValue value) => _dictionary.TryGetValue(key, out value);

            IEnumerator IEnumerable.GetEnumerator() => _dictionary.GetEnumerator();
        }
#endif
    }
}
