using EspDotNet.Commands;
using EspDotNet.Communication;
using EspDotNet.Exceptions;

namespace EspDotNet.Loaders.SoftLoader
{
    // Command bytes follow the esptool serial protocol and the stub flasher:
    // https://docs.espressif.com/projects/esptool/en/latest/esp32/advanced-topics/serial-protocol.html
    public class SoftLoader : ILoader
    {
        // Shared with the ROM loader
        private const byte CmdFlashBegin = 0x02;
        private const byte CmdFlashData = 0x03;
        private const byte CmdFlashEnd = 0x04;
        private const byte CmdMemBegin = 0x05;
        private const byte CmdMemEnd = 0x06;
        private const byte CmdMemData = 0x07;
        private const byte CmdReadReg = 0x0A;
        private const byte CmdChangeBaudRate = 0x0F;
        // Stub-only commands
        private const byte CmdFlashDeflBegin = 0x10;
        private const byte CmdFlashDeflData = 0x11;
        private const byte CmdFlashDeflEnd = 0x12;
        private const byte CmdSpiFlashMd5 = 0x13;
        private const byte CmdEraseFlash = 0xD0;
        private const byte CmdFlashReadBegin = 0xD2;

        private const int Md5Length = 16;

        private readonly byte[] OHAI = { 0x4F, 0x48, 0x41, 0x49 };
        protected readonly Communicator _communicator;
        protected readonly SoftLoaderCommandExecutor _commandExecutor;

        public SoftLoader(Communicator communicator)
        {
            _communicator = communicator;
            _commandExecutor = new SoftLoaderCommandExecutor(communicator);
        }

        /// <summary>
        /// Changes the baud rate for communication in the SoftLoader.
        /// </summary>
        /// <param name="baud">The new baud rate to set.</param>
        /// <param name="oldBaud">The current baud rate.</param>
        /// <param name="token">Cancellation token for the operation.</param>
        /// <exception cref="OperationCanceledException">Thrown if the operation is canceled via the token.</exception>
        public async Task ChangeBaudAsync(int baud, int oldBaud, CancellationToken token)
        {
            var request = new RequestCommandBuilder()
                .WithCommand(CmdChangeBaudRate)
                .AppendPayload(BitConverter.GetBytes(baud))
                .AppendPayload(BitConverter.GetBytes(oldBaud))
                .Build();

            var response = await _commandExecutor.ExecuteCommandAsync(request, token).ConfigureAwait(false);
            if (!response.Success)
                throw new EspCommandException(request.Command, response.Error, "ChangeBaudRate");
        }

        /// <summary>
        /// Calculates and verifies the MD5 checksum of a given flash region.
        /// </summary>
        /// <exception cref="OperationCanceledException">Thrown if the operation is canceled via the token.</exception>
        public async Task<byte[]> SPI_FLASH_MD5(uint address, uint size, CancellationToken token)
        {
            var request = new RequestCommandBuilder()
                .WithCommand(CmdSpiFlashMd5)
                .AppendPayload(BitConverter.GetBytes(address))
                .AppendPayload(BitConverter.GetBytes(size))
                .AppendPayload(BitConverter.GetBytes(0))
                .AppendPayload(BitConverter.GetBytes(0))
                .Build();

            var response = await _commandExecutor.ExecuteCommandAsync(request, token).ConfigureAwait(false);
            if (!response.Success)
                throw new EspCommandException(request.Command, response.Error, "SPI_FLASH_MD5");

            return response.Payload.Take(Md5Length).ToArray(); // Return the MD5 checksum (first 16 bytes)
        }

        public async Task FlashReadBeginAsync(uint address, uint size, uint blockSize, uint inflight, CancellationToken token)
        {
            var request = new RequestCommandBuilder()
                .WithCommand(CmdFlashReadBegin)
                .AppendPayload(BitConverter.GetBytes(address))
                .AppendPayload(BitConverter.GetBytes(size))
                .AppendPayload(BitConverter.GetBytes(blockSize))
                .AppendPayload(BitConverter.GetBytes(inflight))
                .Build();


            var response = await _commandExecutor.ExecuteCommandAsync(request, token).ConfigureAwait(false);
            if (!response.Success)
                throw new EspCommandException(request.Command, response.Error, "FlashReadBegin");
        }

        public async Task FlashReadAckAsync(uint totalBytesReceived, CancellationToken token)
        {
            var ackData = BitConverter.GetBytes(totalBytesReceived);
            await _communicator.WriteFrameAsync(new Frame(ackData), token).ConfigureAwait(false);
            await _communicator.FlushAsync(token).ConfigureAwait(false);
        }

        /// <summary>
        /// Begins the flash process using compressed data.
        /// </summary>
        public async Task FlashDeflBeginAsync(uint size, uint blocks, uint blockSize, uint offset, CancellationToken token)
        {
            var request = new RequestCommandBuilder()
                .WithCommand(CmdFlashDeflBegin)
                .AppendPayload(BitConverter.GetBytes(size))
                .AppendPayload(BitConverter.GetBytes(blocks))
                .AppendPayload(BitConverter.GetBytes(blockSize))
                .AppendPayload(BitConverter.GetBytes(offset))
                .Build();

            var response = await _commandExecutor.ExecuteCommandAsync(request, token).ConfigureAwait(false);
            if (!response.Success)
                throw new EspCommandException(request.Command, response.Error, "FlashDeflBegin");
        }

        /// <summary>
        /// Sends compressed flash data.
        /// </summary>
        public async Task FlashDeflDataAsync(byte[] blockData, uint seq, CancellationToken token)
        {
            var request = new RequestCommandBuilder()
                .WithCommand(CmdFlashDeflData)
                .RequiresChecksum()
                .AppendPayload(BitConverter.GetBytes(blockData.Length))
                .AppendPayload(BitConverter.GetBytes(seq))
                .AppendPayload(BitConverter.GetBytes(0))
                .AppendPayload(BitConverter.GetBytes(0))
                .AppendPayload(blockData)
                .Build();

            var response = await _commandExecutor.ExecuteCommandAsync(request, token).ConfigureAwait(false);
            if (!response.Success)
                throw new EspCommandException(request.Command, response.Error, "FlashDeflData");
        }

        /// <summary>
        /// Ends the flash process using compressed data.
        /// </summary>
        public async Task FlashDeflEndAsync(uint executeFlags, uint entryPoint, CancellationToken token)
        {
            var request = new RequestCommandBuilder()
                .WithCommand(CmdFlashDeflEnd)
                .AppendPayload(BitConverter.GetBytes(executeFlags))
                .AppendPayload(BitConverter.GetBytes(entryPoint))
                .Build();

            var response = await _commandExecutor.ExecuteCommandAsync(request, token).ConfigureAwait(false);
            if (!response.Success)
                throw new EspCommandException(request.Command, response.Error, "FlashDeflEnd");
        }

        /// <summary>
        /// Erases the entire flash memory.
        /// </summary>
        public async Task EraseFlashAsync(CancellationToken token)
        {
            var request = new RequestCommandBuilder()
                .WithCommand(CmdEraseFlash)
                .Build();

            var response = await _commandExecutor.ExecuteCommandAsync(request, token).ConfigureAwait(false);
            if (!response.Success)
                throw new EspCommandException(request.Command, response.Error, "EraseFlash");
        }




        #region Supported by software loader and ROM loaders

        /// <summary>
        /// Begins the flash process.
        /// </summary>
        public virtual async Task FlashBeginAsync(uint size, uint blocks, uint blockSize, uint offset, CancellationToken token)
        {
            var request = new RequestCommandBuilder()
                .WithCommand(CmdFlashBegin)
                .AppendPayload(BitConverter.GetBytes(size))
                .AppendPayload(BitConverter.GetBytes(blocks))
                .AppendPayload(BitConverter.GetBytes(blockSize))
                .AppendPayload(BitConverter.GetBytes(offset))
                .Build();

            var response = await _commandExecutor.ExecuteCommandAsync(request, token).ConfigureAwait(false);
            if (!response.Success)
                throw new EspCommandException(request.Command, response.Error, "FlashBegin");
        }

        /// <summary>
        /// Sends flash data.
        /// </summary>
        public virtual async Task FlashDataAsync(byte[] blockData, uint seq, CancellationToken token)
        {
            var request = new RequestCommandBuilder()
                .WithCommand(CmdFlashData)
                .RequiresChecksum()
                .AppendPayload(BitConverter.GetBytes(blockData.Length))
                .AppendPayload(BitConverter.GetBytes(seq))
                .AppendPayload(BitConverter.GetBytes(0))
                .AppendPayload(BitConverter.GetBytes(0))
                .AppendPayload(blockData)
                .Build();

            var response = await _commandExecutor.ExecuteCommandAsync(request, token).ConfigureAwait(false);
            if (!response.Success)
                throw new EspCommandException(request.Command, response.Error, "FlashData");
        }

        /// <summary>
        /// Ends the flash process.
        /// </summary>
        public virtual async Task FlashEndAsync(uint execute, uint entryPoint, CancellationToken token)
        {
            var request = new RequestCommandBuilder()
                .WithCommand(CmdFlashEnd)
                .AppendPayload(BitConverter.GetBytes(execute))
                .AppendPayload(BitConverter.GetBytes(entryPoint))
                .Build();

            var response = await _commandExecutor.ExecuteCommandAsync(request, token).ConfigureAwait(false);
            if (!response.Success)
                throw new EspCommandException(request.Command, response.Error, "FlashEnd");
        }


        /// <summary>
        /// Begins memory upload.
        /// </summary>
        public virtual async Task MemBeginAsync(uint size, uint blocks, uint blockSize, uint offset, CancellationToken token)
        {
            var request = new RequestCommandBuilder()
                .WithCommand(CmdMemBegin)
                .AppendPayload(BitConverter.GetBytes(size))
                .AppendPayload(BitConverter.GetBytes(blocks))
                .AppendPayload(BitConverter.GetBytes(blockSize))
                .AppendPayload(BitConverter.GetBytes(offset))
                .Build();

            var response = await _commandExecutor.ExecuteCommandAsync(request, token).ConfigureAwait(false);
            if (!response.Success)
                throw new EspCommandException(request.Command, response.Error, "MemBegin");
        }

        /// <summary>
        /// Ends memory upload.
        /// </summary>
        public virtual async Task MemEndAsync(uint execute, uint entryPoint, CancellationToken token)
        {
            var request = new RequestCommandBuilder()
                .WithCommand(CmdMemEnd)
                .AppendPayload(BitConverter.GetBytes(execute))
                .AppendPayload(BitConverter.GetBytes(entryPoint))
                .Build();

            var response = await _commandExecutor.ExecuteCommandAsync(request, token).ConfigureAwait(false);
            if (!response.Success)
                throw new EspCommandException(request.Command, response.Error, "MemEnd");
        }

        /// <summary>
        /// Sends memory data.
        /// </summary>
        public virtual async Task MemDataAsync(byte[] blockData, uint seq, CancellationToken token)
        {
            var request = new RequestCommandBuilder()
                .WithCommand(CmdMemData)
                .RequiresChecksum()
                .AppendPayload(BitConverter.GetBytes(blockData.Length))
                .AppendPayload(BitConverter.GetBytes(seq))
                .AppendPayload(BitConverter.GetBytes(0))
                .AppendPayload(BitConverter.GetBytes(0))
                .AppendPayload(blockData)
                .Build();

            var response = await _commandExecutor.ExecuteCommandAsync(request, token).ConfigureAwait(false);
            if (!response.Success)
                throw new EspCommandException(request.Command, response.Error, "MemData");
        }

        public virtual async Task<uint> ReadRegisterAsync(uint address, CancellationToken token)
        {
            var request = new RequestCommandBuilder()
               .WithCommand(CmdReadReg)
               .AppendPayload(BitConverter.GetBytes(address))
               .Build();

            var response = await _commandExecutor.ExecuteCommandAsync(request, token).ConfigureAwait(false);
            if (!response.Success)
                throw new EspCommandException(request.Command, response.Error, "ReadRegister");

            return response.Value;
        }
        #endregion

        #region Misc

        /// <summary>
        /// Waits for the OHAI message.
        /// </summary>
        public async Task WaitForOHAIAsync(CancellationToken token)
        {
            Frame? frame = await _communicator.ReadFrameAsync(token).ConfigureAwait(false);

            while (frame?.Data.SequenceEqual(OHAI) != true)
            {
                frame = await _communicator.ReadFrameAsync(token).ConfigureAwait(false);
            }
        }

        #endregion


    }
}
