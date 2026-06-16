using EspDotNet.Communication;
using EspDotNet.Exceptions;
using EspDotNet.Loaders.SoftLoader;
using System.Diagnostics;
using System.Security.Cryptography;

namespace EspDotNet.Tools
{
    public class FlashDownloadTool
    {
        public IProgress<float> Progress { get; set; } = new Progress<float>();
        public uint BlockSize { get; set; } = 4096;
        public uint MaxInFlight { get; set; } = 1;

        // Optional diagnostic sink for wire-level events (frame count/size/timing). Used to
        // debug stub/protocol mismatches; safe to leave null in production.
        public Action<string>? OnTrace { get; set; }

        private readonly SoftLoader _softLoader;
        private readonly Communicator _communicator;

        public FlashDownloadTool(SoftLoader softLoader, Communicator communicator)
        {
            _softLoader = softLoader;
            _communicator = communicator;
        }

        public Stream OpenFlashReadStream(uint address, uint size)
        {
            return new FlashReadStream(this, address, size);
        }

        public async Task ReadFlashAsync(uint address, uint size, Stream outputStream, CancellationToken token)
        {
            var flashStream = OpenFlashReadStream(address, size);
            await flashStream.CopyToAsync(outputStream, 81920, token).ConfigureAwait(false);
        }

        public class FlashReadStream : Stream
        {
            private readonly FlashDownloadTool _tool;
            private readonly uint _totalSize;
            private uint _position; // Flash offset
            private readonly Queue<byte> _buffer = new();
            private readonly MD5 _md5 = MD5.Create();

            public FlashReadStream(FlashDownloadTool tool, uint address, uint size)
            {
                _tool = tool;
                _position = address;
                _totalSize = size + address;
            }

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                if (_position >= _totalSize && _buffer.Count == 0)
                    return 0;

                int bytesReturned = 0;

                bytesReturned += ReadFromBuffer(buffer, offset, count);
                bytesReturned += await ReadFromDeviceAsync(buffer, offset + bytesReturned, count - bytesReturned, cancellationToken).ConfigureAwait(false);

                return bytesReturned;
            }

            private int ReadFromBuffer(byte[] buffer, int offset, int count)
            {
                int bytesRead = 0;
                while (_buffer.Count > 0 && bytesRead < count)
                {
                    buffer[offset + bytesRead++] = _buffer.Dequeue();
                }
                return bytesRead;
            }

            private async Task<int> ReadFromDeviceAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                int remainingBytes = (int)(_totalSize - _position);
                if (remainingBytes <= 0)
                    return 0;

                // The bundled stub answers READ_FLASH by sending exactly one data frame of the
                // requested size followed by the MD5 frame, then returns to command mode. It does
                // not implement ack-driven streaming - sending acks here would be parsed by the
                // stub as new (bad) commands and pollute the wire with error status frames. So
                // we request at most BlockSize per call and let CopyToAsync loop for the rest.
                uint thisBlock = Math.Min(_tool.BlockSize, (uint)remainingBytes);

                _md5.Initialize();
                var sw = Stopwatch.StartNew();
                _tool.OnTrace?.Invoke($"FlashReadBegin addr=0x{_position:X} block={thisBlock} (remaining after={remainingBytes - thisBlock})");
                await _tool._softLoader.FlashReadBeginAsync(_position, thisBlock, thisBlock, _tool.MaxInFlight, cancellationToken).ConfigureAwait(false);
                _tool.OnTrace?.Invoke($"FlashReadBegin OK after {sw.ElapsedMilliseconds}ms");

                sw.Restart();
                var frame = await _tool._communicator.ReadFrameAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new IOException("Failed to receive flash data frame.");
                _tool.OnTrace?.Invoke($"data frame len={frame.Data.Length} read in {sw.ElapsedMilliseconds}ms");

                _md5.TransformBlock(frame.Data, 0, frame.Data.Length, null, 0);

                int copyCount = Math.Min(count, frame.Data.Length);
                Array.Copy(frame.Data, 0, buffer, offset, copyCount);
                for (int i = copyCount; i < frame.Data.Length; i++)
                    _buffer.Enqueue(frame.Data[i]);

                _position += (uint)frame.Data.Length;
                _tool.Progress.Report((float)_position / _totalSize);

                _md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                await VerifyMd5Async(_md5, cancellationToken).ConfigureAwait(false);

                return copyCount;
            }


            private async Task VerifyMd5Async(MD5 md5, CancellationToken token)
            {
                // After the data stream the stub sends both a final status response (~10 bytes)
                // and the 16-byte MD5 frame. The order observed on real ESP32 hardware puts the
                // status before the MD5, so skip past any non-16-byte frames until we find the
                // hash. The MD5 has a fixed length, so this is unambiguous.
                byte[]? expectedHash = null;
                int verifyFrameIndex = 0;
                while (expectedHash == null)
                {
                    var frame = await _tool._communicator.ReadFrameAsync(token).ConfigureAwait(false)
                        ?? throw new EspProtocolException("Expected MD5 hash frame after flash read.");
                    _tool.OnTrace?.Invoke($"post-data frame #{verifyFrameIndex++} len={frame.Data.Length} first8={(frame.Data.Length >= 8 ? BitConverter.ToString(frame.Data, 0, 8) : BitConverter.ToString(frame.Data))}");
                    if (frame.Data.Length == 16)
                        expectedHash = frame.Data;
                    // Otherwise it's the trailing status response; drop it and keep reading.
                }

                var computedHash = md5.Hash ?? throw new InvalidOperationException("Hash not computed yet.");

                for (int i = 0; i < 16; i++)
                {
                    if (computedHash[i] != expectedHash[i])
                    {
                        var expected = BitConverter.ToString(expectedHash).Replace("-", "");
                        var computed = BitConverter.ToString(computedHash).Replace("-", "");
                        throw new InvalidOperationException($"MD5 verification failed:\n  Expected: {expected}\n  Computed: {computed}");
                    }
                }
            }

            // Stream boilerplate
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => _totalSize;
            public override long Position { get => _position; set => throw new NotSupportedException(); }
            public override void Flush() => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count).GetAwaiter().GetResult();
        }

    }
}

