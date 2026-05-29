using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EspDotNet.Communication
{
    /*  In slipframing, the following rules apply:
     *  0xC0 => 0xDB 0xDC
     *  0xDC => 0xDB 0xDD
     */

    public class SlipFraming
    {
        private const byte FrameDelimiter = 0xC0;
        private const byte EscapeByte = 0xDB;
        private const byte EscapeFrameDelimiter = 0xDC;
        private const byte EscapeEscapeByte = 0xDD;
        private readonly SerialPort _serialPort;

        // Buffered reads so we pull a chunk from the stream at a time instead of polling per byte.
        private readonly byte[] _readBuffer = new byte[1024];
        private int _readBufferPos;
        private int _readBufferLen;

        public SlipFraming(SerialPort serialPort)
        {
            _serialPort = serialPort;
        }

        public async Task WriteFrameAsync(Frame frame, CancellationToken token)
        {
            byte[] escapedFrame = EscapeFrame(frame);
            _serialPort.BaseStream.WriteByte(FrameDelimiter); // Start of frame
            await _serialPort.BaseStream.WriteAsync(escapedFrame, 0, escapedFrame.Length, token).ConfigureAwait(false);
            _serialPort.BaseStream.WriteByte(FrameDelimiter); // end of frame
            await _serialPort.BaseStream.FlushAsync(token).ConfigureAwait(false); // Ensure all data is sent
        }

        public async Task<Frame?> ReadFrameAsync(CancellationToken token)
        {
            List<byte> escapedFrameBuffer = new List<byte>();

            // In slipframing, all delimiters are replaced, so we can record everything between delimeters and decode it later
            while (true)
            {
                byte currentByte = await ReadByte(token).ConfigureAwait(false);

                if (currentByte == FrameDelimiter)
                {
                    // If we havent recieved any data yet, this is the SOF
                    if (escapedFrameBuffer.Count > 0)
                        return Unescape(escapedFrameBuffer.ToArray());
                }
                else
                {
                    escapedFrameBuffer.Add(currentByte);
                }
            }
        }

        private async Task<byte> ReadByte(CancellationToken token)
        {
            if (_readBufferPos >= _readBufferLen)
                await FillReadBuffer(token).ConfigureAwait(false);

            return _readBuffer[_readBufferPos++];
        }

        // We poll BytesToRead rather than awaiting BaseStream.ReadAsync: on Windows the SerialPort
        // async read does not honor cancellation of an in-flight read, so a missing byte (e.g. a
        // dropped ROM SYNC reply) would block forever and the caller's token would not fire.
        // Polling lets us honor cancellation reliably across platforms.
        private async Task FillReadBuffer(CancellationToken token)
        {
            while (_serialPort.BytesToRead == 0)
            {
                token.ThrowIfCancellationRequested();
                await Task.Delay(5, token).ConfigureAwait(false);
            }

            int available = Math.Min(_serialPort.BytesToRead, _readBuffer.Length);
            _readBufferLen = _serialPort.Read(_readBuffer, 0, available);
            _readBufferPos = 0;

            if (_readBufferLen == 0)
                throw new EndOfStreamException("Serial stream closed while reading a frame.");
        }

        private byte[] EscapeFrame(Frame frame)
        {
            List<byte> buffer = new();

            foreach (byte b in frame.Data)
            {
                if (b == FrameDelimiter)
                {
                    buffer.Add(EscapeByte);
                    buffer.Add(EscapeFrameDelimiter);
                }
                else if (b == EscapeByte)
                {
                    buffer.Add(EscapeByte);
                    buffer.Add(EscapeEscapeByte);
                }
                else
                {
                    buffer.Add(b);
                }
            }
            return buffer.ToArray();
        }

        private Frame? Unescape(byte[] data)
        {
            List<byte> buffer = new List<byte>();

            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] == EscapeByte)
                {
                    i++;
                    if (i >= data.Length) break;

                    if (data[i] == EscapeFrameDelimiter)
                    {
                        buffer.Add(FrameDelimiter);
                    }
                    else if (data[i] == EscapeEscapeByte)
                    {
                        buffer.Add(EscapeByte);
                    }
                }
                else
                {
                    buffer.Add(data[i]);
                }
            }

            return new Frame(buffer.ToArray());
        }
    }
}
