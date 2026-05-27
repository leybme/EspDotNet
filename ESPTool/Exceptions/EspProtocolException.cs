using System;

namespace EspDotNet.Exceptions
{
    /// <summary>
    /// Thrown when the serial protocol is violated: no frame received, a malformed/too-short
    /// response frame, or a response whose command byte does not match the request.
    /// </summary>
    public class EspProtocolException : EspException
    {
        public EspProtocolException(string message) : base(message) { }
        public EspProtocolException(string message, Exception innerException) : base(message, innerException) { }
    }
}
