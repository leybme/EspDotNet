using EspDotNet.Config;
using System;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace EspDotNet.Communication
{
    public class Communicator
    {
        private readonly SerialPort _serialPort;
        private readonly SlipFraming _slipFraming;

        /// <summary>
        /// Optional sink for human-readable diagnostics (pin sequence steps, etc.), useful when
        /// debugging why a particular board does not enter the ROM bootloader.
        /// </summary>
        public IProgress<string> LogProgress { get; set; } = new Progress<string>();

        /// <summary>
        /// Initializes a new Communicator using an existing open SerialPort.
        /// The Communicator does not take ownership of the port and will not open, close or dispose it.
        /// </summary>
        public Communicator(SerialPort serialPort)
        {
            _serialPort = serialPort ?? throw new ArgumentNullException(nameof(serialPort));
            _slipFraming = new SlipFraming(_serialPort);
        }

        /// <summary>
        /// Clears the input buffer of the serial port.
        /// </summary>
        public void ClearBuffer()
        {
            if (_serialPort.IsOpen)
            {
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();
            }
        }

        /// <summary>
        /// Writes a SLIP-encoded frame asynchronously to the stream.
        /// </summary>
        /// <param name="frame">The frame to send.</param>
        /// <param name="token">Cancellation token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        /// <exception cref="OperationCanceledException">Thrown if the operation is canceled via the cancellation token.</exception>
        public async Task WriteFrameAsync(Frame frame, CancellationToken token)
        {
            await _slipFraming.WriteFrameAsync(frame, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads and decodes a SLIP-encoded frame asynchronously from the stream.
        /// Reads until the end of the frame is received.
        /// </summary>
        /// <param name="token">Cancellation token to cancel the operation.</param>
        /// <returns>The frame received, or null if no frame is available.</returns>
        /// <exception cref="OperationCanceledException">Thrown if the operation is canceled via the cancellation token.</exception>
        public async Task<Frame?> ReadFrameAsync(CancellationToken token)
        {
            return await _slipFraming.ReadFrameAsync(token).ConfigureAwait(false);
        }

        public async Task ExecutePinSequenceAsync(List<PinSequenceStep> sequence, CancellationToken token)
        {
            foreach (var step in sequence)
            {
                LogProgress.Report($"Pin sequence step: Dtr={FormatTriState(step.Dtr)}, Rts={FormatTriState(step.Rts)}, Delay={step.Delay.TotalMilliseconds:N0}ms");

                if (step.Dtr != null)
                    _serialPort.DtrEnable = step.Dtr.Value;

                if (step.Rts != null)
                {
                    _serialPort.RtsEnable = step.Rts.Value;

                    // Windows-only quirk: setting RtsEnable on the Windows serial driver can also
                    // clear the DTR line. Re-asserting the current DtrEnable value forces the driver
                    // to restore DTR so the two control lines stay independent (matches esptool's
                    // _setDTR/_setRTS handling on Windows).
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        _serialPort.DtrEnable = _serialPort.DtrEnable;
                }

                await Task.Delay(step.Delay, token).ConfigureAwait(false);
            }
        }

        public async Task<int> ReadRawAsync(byte[] buffer, CancellationToken token)
        {
            return await _serialPort.BaseStream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Writes raw data asynchronously to the serial port.
        /// </summary>
        /// <param name="data">The data to write.</param>
        /// <param name="token">Cancellation token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        /// <exception cref="OperationCanceledException">Thrown if the operation is canceled via the cancellation token.</exception>
        public async Task WriteAsync(byte[] data, CancellationToken token)
        {
            await _serialPort.BaseStream.WriteAsync(data, 0, data.Length, token).ConfigureAwait(false);
        }

        internal async Task FlushAsync(CancellationToken token)
        {
            await _serialPort.BaseStream.FlushAsync(token).ConfigureAwait(false);
        }

        private static string FormatTriState(bool? value) => value is null ? "unchanged" : value.Value.ToString();
    }
}
