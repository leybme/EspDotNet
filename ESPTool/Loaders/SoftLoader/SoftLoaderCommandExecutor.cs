using EspDotNet.Commands;
using EspDotNet.Communication;
using EspDotNet.Exceptions;

namespace EspDotNet.Loaders.SoftLoader
{

    public class SoftLoaderCommandExecutor
    {
        // Response header layout: [direction(1)][command(1)][size(2)][value(4)] = 8 bytes, then payload.
        private const int HeaderSize = 8;

        protected readonly Communicator _communicator;

        public SoftLoaderCommandExecutor(Communicator communicator)
        {
            _communicator = communicator;
        }

        /// <summary>
        /// Sends a request frame and waits for a response frame.
        /// </summary>
        /// <exception cref="OperationCanceledException">Thrown if the operation is canceled via the token.</exception>
        /// <exception cref="EspProtocolException">Thrown if no frame is received, the frame is malformed, or the response command does not match the request.</exception>
        public async Task<ResponseCommand> ExecuteCommandAsync(RequestCommand requestCommand, CancellationToken token)
        {
            _communicator.ClearBuffer();

            // Convert the RequestCommand to a Frame
            Frame requestFrame = RequestToFrame(requestCommand);

            // Write the frame to the communicator
            await _communicator.WriteFrameAsync(requestFrame, token).ConfigureAwait(false);

            // Read the response frame
            Frame responseFrame = await _communicator.ReadFrameAsync(token).ConfigureAwait(false)
                ?? throw new EspProtocolException($"No response frame received for command 0x{requestCommand.Command:X2}.");

            // Convert the response frame back to a ResponseCommand
            var response = FrameToResponse(responseFrame);

            // Check
            if (response.Command != requestCommand.Command)
            {
                throw new EspProtocolException($"Response command 0x{response.Command:X2} did not match request command 0x{requestCommand.Command:X2}.");
            }
            return response;
        }

        private static Frame RequestToFrame(RequestCommand command)
        {
            List<byte> raw = new List<byte>
            {
                command.Direction,
                command.Command
            };
            raw.AddRange(BitConverter.GetBytes(command.Size));
            raw.AddRange(BitConverter.GetBytes(command.Checksum));
            raw.AddRange(command.Payload);
            return new Frame(raw.ToArray());
        }



        private static ResponseCommand FrameToResponse(Frame frame)
        {
            byte[] data = frame.Data;
            if (data.Length < HeaderSize)
                throw new EspProtocolException($"Response frame too short: {data.Length} bytes (need at least {HeaderSize}).");

            ResponseCommand response = new ResponseCommand
            {
                Direction = data[0],
                Command = data[1],
                Size = BitConverter.ToUInt16(data, 2),
                Value = BitConverter.ToUInt32(data, 4),
                Payload = data.Skip(HeaderSize).ToArray(),
            };

            // The soft loader appends a 2-byte status trailer. These 2 fields are switched around
            // when compared to the documentation: payload[Size-1] is the success byte and
            // payload[Size-2] is the error code.
            if (response.Size < 2 || response.Payload.Length < response.Size)
                throw new EspProtocolException($"Response payload too short for status trailer (size field={response.Size}, payload={response.Payload.Length}).");

            response.Success = response.Payload[response.Size - 1] == 0;
            SoftLoaderResponseStatus status = (SoftLoaderResponseStatus)response.Payload[response.Size - 2];
            response.Error = GeneralizeResponseStatus(status);

            if (response.Error != ResponseCommandStatus.NoError)
            {
                response.Success = false;
            }
            return response;
        }

        private static ResponseCommandStatus GeneralizeResponseStatus(SoftLoaderResponseStatus err)
        {
            return err switch
            {
                SoftLoaderResponseStatus.ESP_OK => ResponseCommandStatus.NoError,
                SoftLoaderResponseStatus.ESP_BAD_DATA_LEN => ResponseCommandStatus.Invalid,
                SoftLoaderResponseStatus.ESP_BAD_DATA_CHECKSUM => ResponseCommandStatus.InvalidCRC,
                SoftLoaderResponseStatus.ESP_BAD_BLOCKSIZE => ResponseCommandStatus.BadBlockSize,
                SoftLoaderResponseStatus.ESP_INVALID_COMMAND => ResponseCommandStatus.Failed,
                SoftLoaderResponseStatus.ESP_FAILED_SPI_OP => ResponseCommandStatus.FailedSPIOP,
                SoftLoaderResponseStatus.ESP_FAILED_SPI_UNLOCK => ResponseCommandStatus.FailedSPIUnlock,
                SoftLoaderResponseStatus.ESP_NOT_IN_FLASH_MODE => ResponseCommandStatus.NotInFlashMode,
                SoftLoaderResponseStatus.ESP_INFLATE_ERROR => ResponseCommandStatus.InflateError,
                SoftLoaderResponseStatus.ESP_NOT_ENOUGH_DATA => ResponseCommandStatus.NotEnoughData,
                SoftLoaderResponseStatus.ESP_TOO_MUCH_DATA => ResponseCommandStatus.TooMuchData,
                SoftLoaderResponseStatus.ESP_CMD_NOT_IMPLEMENTED => ResponseCommandStatus.Failed,
                _ => ResponseCommandStatus.Unknown,
            };
        }
    }
}
