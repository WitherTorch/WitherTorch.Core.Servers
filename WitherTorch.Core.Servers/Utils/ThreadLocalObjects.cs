using System.Text;
using System.Threading;

namespace WitherTorch.Core.Servers.Utils
{
    internal static class ThreadLocalObjects
    {
        private static readonly ThreadLocal<StringBuilder> _localBuilder = new ThreadLocal<StringBuilder>(() => new StringBuilder(), trackAllValues: false);

        public static StringBuilder StringBuilder => _localBuilder.Value!;
    }
}
