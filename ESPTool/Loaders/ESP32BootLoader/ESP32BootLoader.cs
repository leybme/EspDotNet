using EspDotNet.Commands;
using EspDotNet.Communication;
using EspDotNet.Exceptions;
using EspDotNet.Loaders;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EspDotNet.Loaders.ESP32BootLoader
{
    // Command bytes follow the esptool serial protocol:
    // https://docs.espressif.com/projects/esptool/en/latest/esp32/advanced-topics/serial-protocol.html
    public class ESP32BootLoader : ILoader
    {
        private const byte CmdFlashBegin = 0x02;
        private const byte CmdFlashData = 0x03;
        private const byte CmdFlashEnd = 0x04;
        private const byte CmdMemBegin = 0x05;
        private const byte CmdMemEnd = 0x06;
        private const byte CmdMemData = 0x07;
        private const byte CmdSync = 0x08;
        private const byte CmdReadReg = 0x0A;
        private const byte CmdChangeBaudRate = 0x0F;

        private readonly Communicator _communicator;
        private readonly BootLoaderCommandExecutor _commandExecutor;

        public ESP32BootLoader(Communicator communicator)
        {
            _communicator = communicator;
            _commandExecutor = new BootLoaderCommandExecutor(communicator);
        }


        /// <summary>
        /// Begins the flash process.
        /// </summary>
        public async Task FlashBeginAsync(uint size, uint blocks, uint blockSize, uint offset, CancellationToken token)
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
        public async Task FlashDataAsync(byte[] blockData, uint seq, CancellationToken token)
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
        public async Task FlashEndAsync(uint execute, uint entryPoint, CancellationToken token)
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
        public async Task MemBeginAsync(uint size, uint blocks, uint blockSize, uint offset, CancellationToken token)
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
        public async Task MemEndAsync(uint execute, uint entryPoint, CancellationToken token)
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
        public async Task MemDataAsync(byte[] blockData, uint seq, CancellationToken token)
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

        /// <summary>
        /// Synchronizes with the loader.
        /// </summary>
        public async Task<bool> SynchronizeAsync(CancellationToken token)
        {
            var request = new RequestCommandBuilder()
                .WithCommand(CmdSync)
                .AppendPayload(new byte[] { 0x07, 0x07, 0x12, 0x20, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55, 0x55 })
                .Build();

            var response = await _commandExecutor.ExecuteCommandAsync(request, token).ConfigureAwait(false);
            return response.Success;
        }

        public async Task<uint> ReadRegisterAsync(uint address, CancellationToken token)
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

        /// <summary>
        /// Changes the baud rate for communication in ESP32.
        /// </summary>
        /// <param name="baud">The new baud rate to set.</param>
        /// <param name="oldBaud">The current baud rate (unused by the ROM loader; kept for interface symmetry).</param>
        /// <param name="token">Cancellation token for the operation.</param>
        /// <exception cref="OperationCanceledException">Thrown if the operation is canceled via the token.</exception>
        public async Task ChangeBaudAsync(int baud, int oldBaud, CancellationToken token)
        {
            var request = new RequestCommandBuilder()
                .WithCommand(CmdChangeBaudRate)
                .AppendPayload(BitConverter.GetBytes(baud))
                .AppendPayload(BitConverter.GetBytes(0))  // second parameter is 0 for the ROM loader
                .Build();

            var response = await _commandExecutor.ExecuteCommandAsync(request, token).ConfigureAwait(false);
            if (!response.Success)
                throw new EspCommandException(request.Command, response.Error, "ChangeBaudRate");
        }
    }
}
