using System.Collections.Generic;

#if NET6_0_OR_GREATER
using System.Collections.Immutable;
using System.Collections.Frozen;
#endif

namespace WitherTorch.Core.Servers.Utils
{
    internal static class EmptyDictionary<TKey, TValue> where TKey : notnull
    {
        private static readonly IReadOnlyDictionary<TKey, TValue> _dictionary;

        public static IReadOnlyDictionary<TKey, TValue> Instance => _dictionary;

        static EmptyDictionary()
        {
#if NET6_0_OR_GREATER
            _dictionary = FrozenDictionary.ToFrozenDictionary(ImmutableDictionary<TKey, TValue>.Empty);
#else
            _dictionary = new Dictionary<TKey, TValue>();
#endif
        }
    }
}
