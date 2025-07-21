using System.Collections.Generic;

namespace WitherTorch.Core.Servers.Utils
{
    internal sealed class KeyEqualityComparer<TKey, TValue> : IEqualityComparer<KeyValuePair<TKey, TValue>> where TKey : notnull
    {
        private static readonly KeyEqualityComparer<TKey, TValue> _defaultComparer = new KeyEqualityComparer<TKey, TValue>(EqualityComparer<TKey>.Default);
        private readonly IEqualityComparer<TKey> _keyComparer;

        public static KeyEqualityComparer<TKey, TValue> Default => _defaultComparer;

        private KeyEqualityComparer(IEqualityComparer<TKey> comparer)
        {
            _keyComparer = comparer;
        }

        public bool Equals(KeyValuePair<TKey, TValue> x, KeyValuePair<TKey, TValue> y) => _keyComparer.Equals(x.Key, y.Key);

        public int GetHashCode(KeyValuePair<TKey, TValue> obj) => obj.Key.GetHashCode();
    }
}
