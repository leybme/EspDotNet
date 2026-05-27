using EspDotNet.Config;
using EspDotNet.Loaders;
using EspDotNet.Tools.Firmware;
using EspDotNet.Utils;

namespace EspDotNet.Tools
{
    public class FlashUploadTool : IUploadTool
    {
        public IProgress<float> Progress { get; set; } = new Progress<float>();
        public uint BlockSize { get; set; } = 1024;
        private readonly ILoader _loader;
        private readonly DeviceConfig _deviceConfig;

        public FlashUploadTool(ILoader loader, DeviceConfig deviceConfig)
        {
            _loader = loader;
            _deviceConfig = deviceConfig;
        }

        public async Task UploadAsync(Stream data, uint offset, uint size, CancellationToken token)
        {
            // Calculate blocks
            uint blocks = (size + BlockSize - 1) / BlockSize;
            await _loader.FlashBeginAsync(size, blocks, BlockSize, offset, token).ConfigureAwait(false);

            // Send data
            for (uint i = 0; i < blocks; i++)
            {
                uint srcIndex = i * BlockSize;
                uint len = Math.Min(BlockSize, size - srcIndex);

                byte[] buffer = new byte[len];
                // ReadExactlyAsync fills the whole buffer, looping over short reads, and throws
                // EndOfStreamException if the stream ends before 'len' bytes (the caller promised 'size').
                await data.ReadExactlyAsync(buffer.AsMemory(0, (int)len), token).ConfigureAwait(false);

                await _loader.FlashDataAsync(buffer, i, token).ConfigureAwait(false);
                Progress.Report((float)(i + 1) / blocks);
            }

            // Sending end is not required here
            // https://docs.espressif.com/projects/esptool/en/latest/esp32/advanced-topics/serial-protocol.html#writing-data
        }

        public async Task UploadAndExecuteAsync(Stream uncompressedData, uint offset, uint unCompressedSize, uint entryPoint, CancellationToken token)
        {
            await UploadAsync(uncompressedData, offset, unCompressedSize, token).ConfigureAwait(false);

            // End memory transfer, 0 means execute, confusing
            await _loader.FlashEndAsync(0, entryPoint, token).ConfigureAwait(false);
        }
    }
}
