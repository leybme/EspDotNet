using EspDotNet.Config;
using System;
using System.Collections.Generic;

namespace EspFlasherWpf
{
    /// <summary>
    /// Boards that use the ESP32-C3/S3 built-in USB-Serial-JTAG peripheral (no external
    /// CP2102/CH340 bridge) need a different DTR/RTS pattern to land in the ROM downloader than
    /// the classic external-bridge auto-reset circuit. The peripheral latches "IO0 low" from DTR
    /// while EN is pulsed via RTS, and critically needs an explicit intermediate step where BOTH
    /// DTR=true AND RTS=true at once - skipping that combined-assert step (e.g. by passing through
    /// DTR=0,RTS=0 instead) produces a plain reboot, never download mode. This is exactly what we
    /// observed: esptool-js's reset.ts (UsbJtagSerialReset) relies on this same transient (1,1)
    /// state existing momentarily between two separate setRTS/setDTR calls, but translating that
    /// JS source into discrete combined-state steps initially lost the transient, so the result
    /// rebooted normally every time instead of entering download mode.
    ///
    /// "USB-Serial-JTAG (explicit DTR+RTS assert)" below is the proven-working sequence from a
    /// real app already flashing this exact board family (VIEWE ESP32-C3 / M5Dial ESP32-S3) via
    /// EspDotNet. "Classic" (esptool-js's ClassicReset, with its real default delays of
    /// 50ms/550ms) is kept as a fallback for boards using an external USB-UART bridge instead.
    /// </summary>
    internal static class BootloaderResetSequences
    {
        public static readonly (string Name, List<PinSequenceStep> Sequence)[] Candidates =
        {
            ("USB-Serial-JTAG (explicit DTR+RTS assert)", new List<PinSequenceStep>
            {
                new PinSequenceStep { Dtr = false, Rts = false, Delay = TimeSpan.FromMilliseconds(100) },
                new PinSequenceStep { Dtr = true,  Rts = false, Delay = TimeSpan.FromMilliseconds(100) }, // arm IO0
                new PinSequenceStep { Dtr = true,  Rts = true,  Delay = TimeSpan.Zero },                  // assert EN
                new PinSequenceStep { Dtr = false, Rts = true,  Delay = TimeSpan.FromMilliseconds(100) },
                new PinSequenceStep { Dtr = false, Rts = false, Delay = TimeSpan.Zero },                  // release EN -> download mode
            }),

            ("Classic (esptool-js ClassicReset, 50ms)", new List<PinSequenceStep>
            {
                new PinSequenceStep { Dtr = false, Rts = true,  Delay = TimeSpan.FromMilliseconds(100) },
                new PinSequenceStep { Dtr = true,  Rts = false, Delay = TimeSpan.FromMilliseconds(50) },
                new PinSequenceStep { Dtr = false, Rts = false, Delay = TimeSpan.Zero },
            }),

            ("Classic (esptool-js ClassicReset, 550ms)", new List<PinSequenceStep>
            {
                new PinSequenceStep { Dtr = false, Rts = true,  Delay = TimeSpan.FromMilliseconds(100) },
                new PinSequenceStep { Dtr = true,  Rts = false, Delay = TimeSpan.FromMilliseconds(550) },
                new PinSequenceStep { Dtr = false, Rts = false, Delay = TimeSpan.Zero },
            }),
        };
    }
}
