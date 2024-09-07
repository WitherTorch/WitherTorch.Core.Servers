using System.Collections.Generic;

namespace WitherTorch.Core.Servers.Utils
{
    internal static class ComparerExtensions
    {
        public static IComparer<T> Reverse<T>(this IComparer<T> comparer)
        {
            return new ReverseComparer<T>(comparer);
        }
    }
}
