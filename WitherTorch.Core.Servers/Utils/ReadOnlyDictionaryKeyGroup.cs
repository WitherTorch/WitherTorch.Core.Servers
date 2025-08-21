using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace WitherTorch.Core.Servers.Utils
{
    internal static class ReadOnlyDictionaryKeyGroup
    {
        public static ReadOnlyDictionaryKeyGroup<TKey, TValue> Create<TKey, TValue>(TKey[] keys, TValue[] values)
            where TKey : notnull
        {
            if (keys.Length != values.Length)
                throw new InvalidOperationException();
            if (keys.Length <= 0)
                return ReadOnlyDictionaryKeyGroup<TKey, TValue>.Empty;
            IEnumerable<KeyValuePair<TKey, TValue>> enumerable
#if NETSTANDARD2_1 || NET8_0_OR_GREATER
                = keys.Zip(values, (key, value) => KeyValuePair.Create(key, value));
#else
                = keys.Zip(values, (key, value) => new KeyValuePair<TKey, TValue>(key, value));
#endif
            return new ReadOnlyDictionaryKeyGroup<TKey, TValue>(CreateReadOnlyDictionary(enumerable), keys);
        }

        public static ReadOnlyDictionaryKeyGroup<TKey, TValue> Create<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>> source)
            where TKey : notnull
            => Create(CreateReadOnlyDictionary(source));

        public static ReadOnlyDictionaryKeyGroup<TKey, TValue> Create<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>> source, Func<IEnumerable<TKey>, TKey[]> keyGroupCreateFunc)
            where TKey : notnull
            => Create(CreateReadOnlyDictionary(source), keyGroupCreateFunc);

        public static ReadOnlyDictionaryKeyGroup<TKey, TValue> Create<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>> source, Action<TKey[]> keyGroupPostProcessAction)
            where TKey : notnull
            => Create(CreateReadOnlyDictionary(source), keyGroupPostProcessAction);

        public static ReadOnlyDictionaryKeyGroup<TKey, TValue> Create<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> dict)
            where TKey : notnull
        {
            if (dict.Count <= 0)
                return ReadOnlyDictionaryKeyGroup<TKey, TValue>.Empty;

            return new ReadOnlyDictionaryKeyGroup<TKey, TValue>(dict, dict.Keys.ToArray());
        }

        public static ReadOnlyDictionaryKeyGroup<TKey, TValue> Create<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> dict, Func<IEnumerable<TKey>, TKey[]> keyGroupCreateFunc)
            where TKey : notnull
        {
            if (dict.Count <= 0)
                return ReadOnlyDictionaryKeyGroup<TKey, TValue>.Empty;

            return new ReadOnlyDictionaryKeyGroup<TKey, TValue>(dict, keyGroupCreateFunc(dict.Keys));
        }

        public static ReadOnlyDictionaryKeyGroup<TKey, TValue> Create<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> dict, Action<TKey[]> keyGroupPostProcessAction)
            where TKey : notnull
        {
            if (dict.Count <= 0)
                return ReadOnlyDictionaryKeyGroup<TKey, TValue>.Empty;

            TKey[] keys = dict.Keys.ToArray();
            keyGroupPostProcessAction.Invoke(keys);
            return new ReadOnlyDictionaryKeyGroup<TKey, TValue>(dict, keys);
        }

        public static ReadOnlyDictionaryKeyGroup<TKey, TValue> Create<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> dict, Action<IReadOnlyDictionary<TKey, TValue>, TKey[]> keyGroupPostProcessAction)
            where TKey : notnull
        {
            if (dict.Count <= 0)
                return ReadOnlyDictionaryKeyGroup<TKey, TValue>.Empty;

            TKey[] keys = dict.Keys.ToArray();
            keyGroupPostProcessAction.Invoke(dict, keys);
            return new ReadOnlyDictionaryKeyGroup<TKey, TValue>(dict, keys);
        }

#pragma warning disable CA1859
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static IReadOnlyDictionary<TKey, TValue> CreateReadOnlyDictionary<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>> source)
            where TKey : notnull
        {
#if NET8_0_OR_GREATER
            return System.Collections.Frozen.FrozenDictionary.ToFrozenDictionary(source);
#else
            using IEnumerator<KeyValuePair<TKey, TValue>> enumerator = source.GetEnumerator();
            if (!enumerator.MoveNext())
                return EmptyDictionary<TKey, TValue>.Instance;
            Dictionary<TKey, TValue> dictionary = new Dictionary<TKey, TValue>();
            do
            {
                KeyValuePair<TKey, TValue> current = enumerator.Current;
                dictionary.Add(current.Key, current.Value);
            } while (enumerator.MoveNext());
            return dictionary;
#endif
        }
#pragma warning restore CA1859
    }

    internal sealed class ReadOnlyDictionaryKeyGroup<TKey, TValue> where TKey : notnull
    {
        public static readonly ReadOnlyDictionaryKeyGroup<TKey, TValue> Empty =
            new ReadOnlyDictionaryKeyGroup<TKey, TValue>(EmptyDictionary<TKey, TValue>.Instance, Array.Empty<TKey>());

        private readonly IReadOnlyDictionary<TKey, TValue> _dict;
        private readonly TKey[] _keys;

        public IReadOnlyDictionary<TKey, TValue> Dictionary => _dict;

        public TKey[] Keys => _keys;

        public ReadOnlyDictionaryKeyGroup(IReadOnlyDictionary<TKey, TValue> dict, TKey[] keys)
        {
            _dict = dict;
            _keys = keys;
        }
    }
}
