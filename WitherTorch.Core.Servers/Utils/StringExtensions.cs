using System;
using System.Runtime.CompilerServices;

namespace WitherTorch.Core.Servers.Utils
{
    internal static class StringExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EndsWith(this string _this, char value)
        {
            int length = _this.Length;
            if (length <= 0)
                return false;
            return _this[length - 1] == value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EndsWithAny(this string _this, params ReadOnlySpan<char> value)
        {
            int length = _this.Length;
            if (length <= 0)
                return false;
            return value.IndexOf(_this[length - 1]) >= 0;
        }
    }
}
