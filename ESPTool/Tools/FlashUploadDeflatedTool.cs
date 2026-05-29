using EspDotNet.Config;
using EspDotNet.Loaders;
using EspDotNet.Loaders.SoftLoader;
using EspDotNet.Tools.Firmware;
using EspDotNet.Utils;
using System;

namespace EspDotNet.Tools
{
    public class FlashUploadDeflatedTool : IUploadTool
    {
        public IProgress<float> Progress { get; set; } = new Progress<float>();
        public uint BlockSize { get; set; } = 1024;
        private readonly SoftLoader _loader;
        private readonly DeviceConfig _deviceConfig;

        public FlashUploadDeflatedTool(SoftLoader loader, DeviceConfig deviceConfig)
        {
            _loader = loader;
            _deviceConfig = deviceConfig;
        }

        public async Task UploadAsync(Stream uncompressedStream, uint offset, uint unCompressedSize, CancellationToken token)
        {
            await SendBlocksAsync(uncompressedStream, offset, unCompressedSize, token).ConfigureAwait(false);

            // FLASH_DEFL_END(flag=1) tells the softloader to finalize the write and stay in
            // flash mode. Without it the softloader keeps the final decompression buffer in RAM
            // and the tail of the image never reaches flash, so the device boots a truncated image.
            await _loader.FlashDeflEndAsync(1, 0, token).ConfigureAwait(false);
        }

        public async Task UploadAndExecuteAsync(Stream uncompressedData, uint offset, uint unCompressedSize, uint entryPoint, CancellationToken token)
        {
            await SendBlocksAsync(uncompressedData, offset, unCompressedSize, token).ConfigureAwait(false);

            // FLASH_DEFL_END(flag=0) finalizes the write AND reboots into the user image.
            await _loader.FlashDeflEndAsync(0, entryPoint, token).ConfigureAwait(false);
        }

        private async Task SendBlocksAsync(Stream uncompressedStream, uint offset, uint unCompressedSize, CancellationToken token)
        {
            MemoryStream compressedStream = new MemoryStream();
            ZlibCompressionHelper.CompressToZlibStream(uncompressedStream, compressedStream);
            compressedStream.Position = 0;

            UInt32 compressedSize = (UInt32)compressedStream.Length;
            UInt32 blockSize = (UInt32)BlockSize;
            UInt32 blocks = compressedSize / blockSize;
            if (compressedSize % blockSize != 0)
                blocks++;

            await _loader.FlashDeflBeginAsync(unCompressedSize, blocks, BlockSize, offset, token).ConfigureAwait(false);

            for (uint i = 0; i < blocks; i++)
            {
                uint srcIndex = i * BlockSize;
                uint len = Math.Min(BlockSize, compressedSize - srcIndex);

                byte[] buffer = new byte[len];
                // Fill the whole block, looping over short reads; throws if the stream ends early.
                await compressedStream.ReadExactlyAsync(buffer.AsMemory(0, (int)len), token).ConfigureAwait(false);

                await _loader.FlashDeflDataAsync(buffer, i, token).ConfigureAwait(false);
                Progress.Report((float)(i + 1) / blocks);
            }
        }
    }
}
