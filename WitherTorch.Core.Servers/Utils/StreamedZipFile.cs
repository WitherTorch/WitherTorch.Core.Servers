using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Ionic.Zip;

using WitherTorch.Core.Runtime;

namespace WitherTorch.Core.Servers.Utils
{
    internal sealed class StreamedZipFile : IDisposable
    {
        private readonly Stream _stream;
        private readonly ZipFile _file;
        private readonly SemaphoreSlim _semaphore;

        public int Count => _file.Count;
        public ZipFile ZipFile => _file;

        public StreamedZipFile(byte[] buffer)
        {
            _stream = new MemoryStream(buffer, writable: false);
            _file = ZipFile.Read(_stream, new ReadOptions() { Encoding = Encoding.UTF8 });
            _semaphore = new SemaphoreSlim(1, 1);
        }

        public StreamedZipFile(string filename, int bufferSize)
        {
            _stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
            _file = ZipFile.Read(_stream, new ReadOptions() { Encoding = Encoding.UTF8 });
            _semaphore = new SemaphoreSlim(1, 1);
        }

        public StreamedZipFile(ITempFileInfo tempFile, int bufferSize)
        {
            _stream = tempFile.Open(FileAccess.Read, bufferSize, useAsync: true);
            _file = ZipFile.Read(_stream, new ReadOptions() { Encoding = Encoding.UTF8 });
            _semaphore = new SemaphoreSlim(1, 1);
        }

        public ZipEntry this[int index] => _file[index];

        public Task WaitForEnterExtractLockAsync(CancellationToken cancellationToken) => _semaphore.WaitAsync(cancellationToken);

        public void LeaveExtractLock() => _semaphore.Release();

        public void Dispose()
        {
            _file.Dispose();
            _stream.Dispose();
            _semaphore.Dispose();
        }
    }
}
