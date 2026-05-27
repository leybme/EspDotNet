namespace EspDotNet.Commands
{
    public class RequestCommandBuilder
    {
        // The checksum seed defined by the esptool serial protocol.
        private const byte ChecksumSeed = 0xEF;
        // FLASH_DATA/MEM_DATA payloads start with a 16-byte header (4 x uint32); the checksum is
        // computed only over the data that follows it.
        private const int ChecksumDataOffset = 16;

        private readonly RequestCommand _request;
        private readonly List<byte> _payload = new();

        public RequestCommandBuilder()
        {
            _request = new RequestCommand();
        }

        /// <summary>
        /// Sets the command.
        /// </summary>
        /// <param name="cmd">The command byte.</param>
        public RequestCommandBuilder WithCommand(byte cmd)
        {
            _request.Command = cmd;
            return this;
        }

        /// <summary>
        /// Appends a byte array to the payload.
        /// </summary>
        public RequestCommandBuilder AppendPayload(byte[] payloadPart)
        {
            _payload.AddRange(payloadPart);
            return this;
        }

        /// <summary>
        /// Sets whether a checksum is required.
        /// </summary>
        /// <param name="checksumRequired">True if a checksum is required, false otherwise.</param>
        public RequestCommandBuilder RequiresChecksum(bool checksumRequired = true)
        {
            _request.ChecksumRequired = checksumRequired;
            return this;
        }

        /// <summary>
        /// Sets the direction byte.
        /// </summary>
        /// <param name="direction">The direction byte.</param>
        public RequestCommandBuilder WithDirection(byte direction)
        {
            _request.Direction = direction;
            return this;
        }

        /// <summary>
        /// Builds and returns the final RequestCMD object.
        /// </summary>
        public RequestCommand Build()
        {
            _request.Payload = _payload.ToArray();
            _request.Size = (ushort)_payload.Count;

            if (_request.ChecksumRequired)
            {
                CalculateChecksum();
            }
            return _request;
        }

        /// <summary>
        /// Calculates the checksum for the payload if required.
        /// </summary>
        private void CalculateChecksum()
        {
            _request.Checksum = ChecksumSeed;

            for (int i = ChecksumDataOffset; i < _request.Payload.Length; i++)
            {
                _request.Checksum ^= _request.Payload[i];
            }
        }
    }
}
