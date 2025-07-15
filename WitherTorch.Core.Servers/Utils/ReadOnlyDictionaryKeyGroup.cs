using System;
using System.Collections.Generic;
using System.Linq;

namespace WitherTorch.Core.Servers.Utils
{
    internal sealed class ReadOnlyDictionaryKeyGroup<TKey, TValue> where TKey : notnull
    {
        public static readonly ReadOnlyDictionaryKeyGroup<TKey, TValue> Empty =
            new ReadOnlyDictionaryKeyGroup<TKey, TValue>(EmptyDictionary<TKey, TValue>.Instance, Array.Empty<TKey>());

        private readonly IReadOnlyDictionary<TKey, TValue> _dict;
        private readonly TKey[] _keys;

        public IReadOnlyDictionary<TKey, TValue> Dictionary => _dict;

        public TKey[] Keys => _keys;

        private ReadOnlyDictionaryKeyGroup(IReadOnlyDictionary<TKey, TValue> dict, TKey[] keys)
        {
            _dict = dict;
            _keys = keys;
        }

        public ReadOnlyDictionaryKeyGroup(IReadOnlyDictionary<TKey, TValue> dict)
        {
            _dict = dict;
            _keys = dict.Count <= 0 ? Array.Empty<TKey>() : dict.Keys.ToArray();
        }

        public ReadOnlyDictionaryKeyGroup(IReadOnlyDictionary<TKey, TValue> dict, Func<IEnumerable<TKey>, TKey[]> keyGroupCreateFunc)
        {
            _dict = dict;
            _keys = keyGroupCreateFunc.Invoke(dict.Count == 0 ? Enumerable.Empty<TKey>() : dict.Keys);
        }

        public ReadOnlyDictionaryKeyGroup(IReadOnlyDictionary<TKey, TValue> dict, Action<TKey[]> keyGroupPostProcessAction)
        {
            _dict = dict;
            if (dict.Count <= 0)
            {
                _keys = Array.Empty<TKey>();
                return;
            }
            TKey[] keys = dict.Keys.ToArray();
            keyGroupPostProcessAction.Invoke(keys);
            _keys = keys;
        }

        public ReadOnlyDictionaryKeyGroup(IReadOnlyDictionary<TKey, TValue> dict, Action<IReadOnlyDictionary<TKey, TValue>, TKey[]> keyGroupPostProcessAction)
        {
            _dict = dict;
            if (dict.Count <= 0)
            {
                _keys = Array.Empty<TKey>();
                return;
            }
            TKey[] keys = dict.Keys.ToArray();
            keyGroupPostProcessAction.Invoke(dict, keys);
            _keys = keys;
        }
    }
}
