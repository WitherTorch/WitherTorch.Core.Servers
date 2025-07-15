using System;
using System.Runtime.CompilerServices;

namespace WitherTorch.Core.Servers.Utils
{
    internal static class VersionStringHelper
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe string ReplaceOnce(string source, char oldChar, char newChar)
        {
            ReadOnlySpan<char> sourceSpan = source.AsSpan();
            int indexOf = sourceSpan.IndexOf(oldChar);
            if (indexOf < 0)
                return source;

#if NET8_0_OR_GREATER
            return string.Concat(sourceSpan.Slice(0, indexOf), new Span<char>(&newChar, 1), sourceSpan.Slice(indexOf + 1));
#else
            string result = new string('\0', source.Length);
            fixed (char* ptrSource = sourceSpan, ptrResult = result)
            {
                Unsafe.CopyBlockUnaligned(ptrResult, ptrSource, (uint)indexOf * sizeof(char));
                Unsafe.CopyBlockUnaligned(ptrResult + indexOf + 1, ptrSource + indexOf + 1, (uint)(sourceSpan.Length - indexOf - 1) * sizeof(char));
                ptrResult[indexOf] = newChar;
            }
            return result;
#endif
        }
    }
}
