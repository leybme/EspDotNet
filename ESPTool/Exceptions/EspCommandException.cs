using EspDotNet.Commands;

namespace EspDotNet.Exceptions
{
    /// <summary>
    /// Thrown when the device returns an unsuccessful status for a command. Carries the command
    /// byte and the generalized device status so callers can react programmatically.
    /// </summary>
    public class EspCommandException : EspException
    {
        /// <summary>The command byte that failed (see the esptool serial protocol).</summary>
        public byte Command { get; }

        /// <summary>The generalized device status returned for the command.</summary>
        public ResponseCommandStatus Status { get; }

        public EspCommandException(byte command, ResponseCommandStatus status, string operation)
            : base($"{operation} failed (command 0x{command:X2}): {status}")
        {
            Command = command;
            Status = status;
        }
    }
}
