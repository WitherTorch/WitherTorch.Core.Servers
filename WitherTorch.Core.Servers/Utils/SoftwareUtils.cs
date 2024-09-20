using System.Runtime.CompilerServices;

namespace WitherTorch.Core.Servers.Utils
{
    internal sealed class SoftwareUtils
    {
#if NET5_0_OR_GREATER
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
        public static string GetReadableVersionString(string minecraftVersion, string secondVersion)
        {
            if (string.IsNullOrEmpty(secondVersion))
                return minecraftVersion;
#if NET5_0_OR_GREATER
            int lengthA = minecraftVersion.Length;
            int lengthB = secondVersion.Length;
            string result = new string('-', lengthA + lengthB + 1);
            unsafe
            {
                fixed (char* resultPointer = result, aPointer = minecraftVersion, bPointer = secondVersion)
                {
                    Unsafe.CopyBlock(resultPointer, aPointer, unchecked((uint)(sizeof(char) * lengthA)));
                    Unsafe.CopyBlock(resultPointer + lengthA + 1, bPointer, unchecked((uint)(sizeof(char) * lengthB)));
                }
            }
            return result;
#else
            return string.Concat(minecraftVersion, "-", secondVersion);
#endif
        }
    }
}
