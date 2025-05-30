﻿using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

#if NET8_0_OR_GREATER
using System.Collections.Immutable;
using System.Collections.Frozen;
#endif

namespace WitherTorch.Core.Servers.Utils
{
    internal static class EmptyDictionary<TKey, TValue> where TKey : notnull
    {
        private static readonly IReadOnlyDictionary<TKey, TValue> _instance;

        public static IReadOnlyDictionary<TKey, TValue> Instance => _instance;

        static EmptyDictionary()
        {
#if NET8_0_OR_GREATER
            _instance = FrozenDictionary.ToFrozenDictionary(ImmutableDictionary<TKey, TValue>.Empty);
#else
            _instance = new EmptyDictionaryImpl();
#endif
        }

#if !NET8_0_OR_GREATER
        private sealed class EmptyDictionaryImpl : IReadOnlyDictionary<TKey, TValue>
        {
            public TValue this[TKey key] => throw new KeyNotFoundException();

            public IEnumerable<TKey> Keys => Enumerable.Empty<TKey>();

            public IEnumerable<TValue> Values => Enumerable.Empty<TValue>();

            public int Count => 0;

            public bool ContainsKey(TKey key) => false;

            public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() 
                => Enumerable.Empty<KeyValuePair<TKey, TValue>>().GetEnumerator();

#pragma warning disable CS8601
            public bool TryGetValue(TKey key, out TValue value)
            {
                value = default;
                return false;
            }
#pragma warning restore CS8601

            IEnumerator IEnumerable.GetEnumerator() 
                => Enumerable.Empty<KeyValuePair<TKey, TValue>>().GetEnumerator();

        }
#endif
    }
}
