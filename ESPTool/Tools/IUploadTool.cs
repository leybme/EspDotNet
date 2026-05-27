namespace EspDotNet.Tools
{
    public interface IUploadTool
    {
        public IProgress<float> Progress { get; set; }
        Task UploadAsync(Stream data, uint offset, uint size, CancellationToken token);
        Task UploadAndExecuteAsync(Stream uncompressedData, uint offset, uint unCompressedSize, uint entryPoint, CancellationToken token);
    }
}
