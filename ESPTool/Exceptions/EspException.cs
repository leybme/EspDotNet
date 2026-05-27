using System;

namespace EspDotNet.Exceptions
{
    /// <summary>
    /// Base type for all EspDotNet-specific exceptions.
    /// </summary>
    public class EspException : Exception
    {
        public EspException(string message) : base(message) { }
        public EspException(string message, Exception innerException) : base(message, innerException) { }
    }
}
