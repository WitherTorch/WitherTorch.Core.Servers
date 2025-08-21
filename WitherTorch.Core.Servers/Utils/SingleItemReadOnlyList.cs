using System;
using System.Collections;
using System.Collections.Generic;

namespace WitherTorch.Core.Servers.Utils
{
    internal sealed class SingleItemReadOnlyList<T> : IReadOnlyList<T>
    {
        private readonly T _item;

        public SingleItemReadOnlyList(T item) => _item = item;

        public T this[int index]
        {
            get
            {
                if (index != 0)
                    throw new IndexOutOfRangeException();
                return _item;
            }
        }

        public int Count => 1;

        public IEnumerator<T> GetEnumerator() => new Enumerator(_item);

        IEnumerator IEnumerable.GetEnumerator() => new Enumerator(_item);

        private sealed class Enumerator : IEnumerator<T>
        {
            private readonly T _item;

            private int _index = -1;

            public Enumerator(T item) => _item = item;

            public T Current
            {
                get
                {
                    if (_index != 0)
                        throw new InvalidOperationException();
                    return _item;
                }
            }

            object? IEnumerator.Current => Current;

            public void Dispose() { }

            public bool MoveNext()
            {
                switch (_index + 1)
                {
                    case 0:
                        _index = 0;
                        return true;
                    default:
                        _index = 1;
                        return false;
                }
            }

            public void Reset() => _index = -1;
        }
    }
}
