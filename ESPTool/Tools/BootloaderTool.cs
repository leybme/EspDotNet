using EspDotNet.Communication;
using EspDotNet.Config;
using EspDotNet.Exceptions;
using EspDotNet.Loaders.ESP32BootLoader;
using System.Text.RegularExpressions;
using System.Text;

namespace EspDotNet.Tools
{
    public class BootloaderTool
    {
        private readonly Communicator _communicator;
        private readonly List<PinSequenceStep> _bootloaderSequence;

        /// <summary>
        /// Optional sink for human-readable diagnostics (attempt counters, the raw ROM banner
        /// received, sync outcomes), useful when a board fails to enter the ROM bootloader.
        /// </summary>
        public IProgress<string> LogProgress { get; set; } = new Progress<string>();

        public BootloaderTool(Communicator communicator, List<PinSequenceStep> bootloaderSequence)
        {
            _communicator = communicator;
            _bootloaderSequence = bootloaderSequence;
        }

        // A single auto-reset pulse can miss landing the chip in the ROM downloader - cap-charge
        // timing on the board's reset circuit varies between resets. esptool.py copes with this by
        // retrying the whole reset cycle (not just SYNC) several times; do the same here instead of
        // only redoing SYNC against a chip that may have booted straight into its normal app.
        private const int MaxBootloaderAttempts = 5;

        public async Task<ESP32BootLoader> StartBootloaderAsync(CancellationToken token = default)
        {
            EspProtocolException? lastError = null;

            for (int attempt = 1; attempt <= MaxBootloaderAttempts; attempt++)
            {
                token.ThrowIfCancellationRequested();
                LogProgress.Report($"Bootloader entry attempt {attempt}/{MaxBootloaderAttempts}: running pin sequence...");

                await _communicator.ExecutePinSequenceAsync(_bootloaderSequence, token).ConfigureAwait(false);

                var (bannerOk, bannerText) = await TryReadBootStartup(token).ConfigureAwait(false);
                LogProgress.Report($"Received after reset: \"{bannerText}\"");

                if (!bannerOk)
                {
                    lastError = new EspProtocolException($"BootLoader message not verified. Last received: \"{bannerText}\"");
                    continue;
                }

                var bootloader = new ESP32BootLoader(_communicator);

                if (await Synchronize(bootloader, token).ConfigureAwait(false))
                    return bootloader;

                LogProgress.Report("SYNC did not get a reply within the retry window.");
                lastError = new EspProtocolException("Failed to synchronize with bootloader");
            }

            throw lastError!;
        }

        // "boot:0x.." alone is printed on every reset, download mode or not - only
        // "waiting for download" confirms the ROM actually entered the download loader.
        // Without requiring it, a device that reset into its normal app (e.g. no working
        // auto-reset circuit) was treated as bootloader-ready and only failed ~10s later
        // with an opaque "Failed to synchronize" once every SYNC attempt timed out.
        private static readonly Regex BootBannerRegex = new Regex("boot:(0x[0-9a-fA-F]+).*waiting for download", RegexOptions.Singleline);

        private async Task<(bool Success, string Text)> TryReadBootStartup(CancellationToken token)
        {
            // Bound the wait: a board that never prints anything (e.g. wrong reset polarity)
            // must not hang this attempt forever - it should fall through to the next retry.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(1));

            var buffer = new byte[4096];
            int read;
            try
            {
                read = await _communicator.ReadRawAsync(buffer, timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                return (false, string.Empty);
            }

            if (read > 0)
            {
                var data = new byte[read];
                Array.Copy(buffer, data, read);
                string text = Encoding.ASCII.GetString(data);
                return (BootBannerRegex.Match(text).Success, text);
            }
            return (false, string.Empty);
        }

        private async Task<bool> Synchronize(ESP32BootLoader loader, CancellationToken token)
        {
            for (int tryNo = 0; tryNo < 100; tryNo++)
            {
                token.ThrowIfCancellationRequested();

                // Try to sync for 100ms.
                using CancellationTokenSource cts = new CancellationTokenSource();

                // Register the token and store the registration to dispose of it later
                using CancellationTokenRegistration ctr = token.Register(() => cts.Cancel());

                cts.CancelAfter(100); // Cancel after 100ms

                try
                {
                    if(await loader.SynchronizeAsync(cts.Token).ConfigureAwait(false))
                        return true;
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    ctr.Dispose();
                }
            }

            return false;
        }
    }

}