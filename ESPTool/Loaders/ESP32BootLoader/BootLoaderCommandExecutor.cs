using EspDotNet.Commands;
using EspDotNet.Communication;
using EspDotNet.Exceptions;

namespace EspDotNet.Loaders.ESP32BootLoader
{
    public class BootLoaderCommandExecutor
    {
        // Response header layout: [direction(1)][command(1)][size(2)][value(4)] = 8 bytes, then payload.
        private const int HeaderSize = 8;

        protected readonly Communicator _communicator;

        public BootLoaderCommandExecutor(Communicator communicator)
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

            // The ROM loader emits several leftover SYNC replies, and DiscardInBuffer cannot catch
            // the ones still in flight, so the first frame after a command is often a stale 0x08
            // response. Like esptool, read past any frame whose command does not match the request
            // (or that is malformed) until the matching response arrives. The per-read timeout in
            // SlipFraming bounds this loop, so a genuinely missing response still surfaces.
            while (true)
            {
                Frame responseFrame = await _communicator.ReadFrameAsync(token).ConfigureAwait(false)
                    ?? throw new EspProtocolException($"No response frame received for command 0x{requestCommand.Command:X2}.");

                ResponseCommand response;
                try
                {
                    response = FrameToResponse(responseFrame);
                }
                catch (EspProtocolException)
                {
                    continue; // malformed/foreign frame (e.g. a truncated stale reply); keep reading
                }

                if (response.Command != requestCommand.Command)
                    continue; // stale/foreign frame (e.g. a leftover SYNC reply); keep reading

                return response;
            }
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

            // The ROM loader appends a status trailer; the bootloader uses payload[Size-4] as the
            // success byte and payload[Size-3] as the error code.
            if (response.Size < 4 || response.Payload.Length < response.Size)
                throw new EspProtocolException($"Response payload too short for status trailer (size field={response.Size}, payload={response.Payload.Length}).");

            response.Success = response.Payload[response.Size - 4] == 0;
            BootLoaderResponseStatus status = (BootLoaderResponseStatus)response.Payload[response.Size - 3];
            response.Error = GeneralizeResponseStatus(status);
            return response;
        }


        private static ResponseCommandStatus GeneralizeResponseStatus(BootLoaderResponseStatus err)
        {
            return err switch
            {
                BootLoaderResponseStatus.Invalid => ResponseCommandStatus.Invalid,
                BootLoaderResponseStatus.Failed => ResponseCommandStatus.Failed,
                BootLoaderResponseStatus.InvalidCRC => ResponseCommandStatus.InvalidCRC,
                BootLoaderResponseStatus.WriteError => ResponseCommandStatus.WriteError,
                BootLoaderResponseStatus.ReadError => ResponseCommandStatus.ReadError,
                BootLoaderResponseStatus.ReadLenthError => ResponseCommandStatus.ReadLenthError,
                BootLoaderResponseStatus.DeflateError => ResponseCommandStatus.DeflateError,
                _ => ResponseCommandStatus.Unknown,
            };
        }
    }
}
