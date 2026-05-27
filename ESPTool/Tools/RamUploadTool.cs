using EspDotNet.Config;
using EspDotNet.Loaders;
using EspDotNet.Loaders.SoftLoader;
using EspDotNet.Tools.Firmware;

namespace EspDotNet.Tools
{
    public class RamUploadTool : IUploadTool
    {
        public IProgress<float> Progress { get; set; } = new Progress<float>();
        private readonly ILoader _loader;
        private readonly DeviceConfig _deviceConfig;

        public RamUploadTool(ILoader loader, DeviceConfig deviceConfig)
        {
            _loader = loader;
            _deviceConfig = deviceConfig;
        }

        public async Task UploadAsync(Stream data, uint offset, uint size, CancellationToken token)
        {
            // Calculate blocks
            uint blockSize = (uint)_deviceConfig.FlashBlockSize;
            uint blocks = (size + blockSize - 1) / blockSize;
            await _loader.MemBeginAsync(size, blocks, blockSize, offset, token).ConfigureAwait(false);

            // Send data
            for (uint i = 0; i < blocks; i++)
            {
                uint srcIndex = i * blockSize;
                uint len = Math.Min(blockSize, size - srcIndex);

                byte[] buffer = new byte[len];
                // Fill the whole block, looping over short reads; throws if the stream ends early.
                await data.ReadExactlyAsync(buffer.AsMemory(0, (int)len), token).ConfigureAwait(false);

                await _loader.MemDataAsync(buffer, i, token).ConfigureAwait(false);
                Progress.Report((float)(i + 1) / blocks);
            }

            // Sending end is not required here
            // https://docs.espressif.com/projects/esptool/en/latest/esp32/advanced-topics/serial-protocol.html#writing-data
        }

        public async Task UploadAndExecuteAsync(Stream uncompressedData, uint offset, uint unCompressedSize, uint entryPoint, CancellationToken token)
        {
            await UploadAsync(uncompressedData, offset, unCompressedSize, token).ConfigureAwait(false);

            // End memory transfer, 0 means execute, confusing
            await _loader.MemEndAsync(0, entryPoint, token).ConfigureAwait(false);
        }
    }

}
