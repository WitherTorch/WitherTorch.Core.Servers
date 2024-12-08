using System;
using System.Collections.Generic;

namespace WitherTorch.Core.Servers.Utils
{
    internal sealed class ReverseComparer<T> : IComparer<T>
    {
        private readonly IComparer<T> _comparer;

        public ReverseComparer(IComparer<T> comparer)
        {
            if (comparer is null)
                throw new ArgumentException(nameof(comparer));
            _comparer = comparer;
        }

        public int Compare(T? x, T? y)
        {
            x = ObjectUtils.ThrowIfNull(x, nameof(x));
            y = ObjectUtils.ThrowIfNull(y, nameof(y));
            int result = _comparer.Compare(x, y);
            return -result;
        }
    }
}
