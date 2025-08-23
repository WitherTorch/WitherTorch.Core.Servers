namespace WitherTorch.Core.Servers
{
    internal static class Constants
    {
        public const int DefaultFileStreamBufferSize = 4096;
        public const int DefaultPooledBufferSize = 131072; // 原始值是 81920，但因為 ArrayPool 只會取二的次方大小，所以選擇了 131072 作為實際大小
        public const string UserAgent = @"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/96.0.4664.55 Safari/537.36";
    }
}
