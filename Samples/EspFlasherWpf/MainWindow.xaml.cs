using EspDotNet;
using EspDotNet.Communication;
using EspDotNet.Config;
using EspDotNet.Tools;
using EspDotNet.Tools.Firmware;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace EspFlasherWpf
{
    public partial class MainWindow : Window
    {
        private CancellationTokenSource _cts;

        public MainWindow()
        {
            InitializeComponent();
            RefreshPorts();
        }

        private void RefreshPortsButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshPorts();
        }

        private void RefreshPorts()
        {
            var ports = SerialPort.GetPortNames().OrderBy(p => p).ToList();
            PortComboBox.ItemsSource = ports;
            if (ports.Count > 0)
                PortComboBox.SelectedIndex = 0;
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Firmware binary (*.bin)|*.bin|All files (*.*)|*.*",
                Title = "Select firmware.bin"
            };

            if (dialog.ShowDialog(this) == true)
                FirmwarePathTextBox.Text = dialog.FileName;
        }

        private async void UploadButton_Click(object sender, RoutedEventArgs e)
        {
            string portName = PortComboBox.SelectedItem as string;
            string firmwarePath = FirmwarePathTextBox.Text;

            if (string.IsNullOrWhiteSpace(portName))
            {
                MessageBox.Show(this, "Select a serial port first.", "EspFlasherWpf", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(firmwarePath) || !File.Exists(firmwarePath))
            {
                MessageBox.Show(this, "Select a valid firmware.bin file first.", "EspFlasherWpf", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            uint offset;
            string offsetText = OffsetTextBox.Text.Trim();
            if (offsetText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                offsetText = offsetText.Substring(2);

            if (!uint.TryParse(offsetText, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out offset))
            {
                MessageBox.Show(this, "Offset must be a hex value, e.g. 0x10000.", "EspFlasherWpf", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SetBusy(true);
            _cts = new CancellationTokenSource();

            try
            {
                await FlashFirmwareAsync(portName, firmwarePath, offset, _cts.Token).ConfigureAwait(true);
                Log("Done. Device reset and booting new firmware.");
            }
            catch (OperationCanceledException)
            {
                Log("Upload cancelled.");
            }
            catch (Exception ex)
            {
                Log($"ERROR: {ex.Message}");
                MessageBox.Show(this, ex.Message, "Upload failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                SetBusy(false);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
        }

        private async Task FlashFirmwareAsync(string portName, string firmwarePath, uint offset, CancellationToken token)
        {
            // Bundled device definitions + bootloader/reset pin sequences.
            var config = ConfigProvider.LoadDefaultConfig();

            using (var port = new SerialPort(portName, 115200))
            {
                port.Open();
                var communicator = new Communicator(port)
                {
                    LogProgress = new Progress<string>(Log)
                };

                var bootloader = await EnterBootloaderAsync(communicator, token).ConfigureAwait(true);

                Log("Detecting chip...");
                var chipDetectTool = new ChipTypeDetectTool(bootloader, config);
                var deviceConfig = await chipDetectTool.DetectAndGetDeviceConfigAsync(token).ConfigureAwait(true);
                Log($"Detected: {deviceConfig.ChipType}");

                // This sample targets ESP32-C3 / ESP32-S3, but any chip with a bundled stub will work.
                if (deviceConfig.ChipType != ChipTypes.ESP32c3 && deviceConfig.ChipType != ChipTypes.ESP32s3)
                    Log($"Warning: this sample was written for ESP32-C3/S3, continuing anyway with {deviceConfig.ChipType}.");

                Log("Uploading stub loader to RAM...");
                var stub = DefaultFirmwareProviders.GetSoftloaderForDevice(deviceConfig.ChipType);
                var ramUploadTool = new RamUploadTool(bootloader, deviceConfig);
                var softLoaderTool = new SoftLoaderTool(communicator, ramUploadTool);
                var softLoader = await softLoaderTool.StartAsync(stub, token).ConfigureAwait(true);

                Log($"Flashing {Path.GetFileName(firmwarePath)} at 0x{offset:X}...");
                byte[] firmwareBytes = File.ReadAllBytes(firmwarePath);
                var firmwareProvider = new FirmwareProvider(
                    entryPoint: 0,
                    segments: new List<IFirmwareSegmentProvider>
                    {
                        new FirmwareSegmentProvider(offset, firmwareBytes)
                    });

                var uploadTool = new FlashUploadDeflatedTool(softLoader, deviceConfig);
                var firmwareUploadTool = new FirmwareUploadTool(uploadTool)
                {
                    Progress = new Progress<float>(p => UploadProgressBar.Value = p * 100)
                };
                await firmwareUploadTool.UploadFirmwareAsync(firmwareProvider, token).ConfigureAwait(true);

                Log("Resetting device...");
                var resetTool = new ResetDeviceTool(communicator, config.ResetSequence);
                await resetTool.ResetAsync(token).ConfigureAwait(true);
            }
        }

        // Some boards' DTR/RTS-to-reset mapping isn't known in advance (e.g. ESP32-C3/S3 native
        // USB-Serial-JTAG peripherals latch download-mode intent from the DTR/RTS edge sequence
        // itself, differently from the classic external-bridge auto-reset circuit). Try each known
        // candidate in turn - toggling these control lines is harmless - and keep whichever works.
        private async Task<EspDotNet.Loaders.ESP32BootLoader.ESP32BootLoader> EnterBootloaderAsync(Communicator communicator, CancellationToken token)
        {
            Exception lastError = null!;

            foreach (var (name, sequence) in BootloaderResetSequences.Candidates)
            {
                Log($"Entering ROM bootloader using '{name}' reset sequence...");
                var bootloaderTool = new BootloaderTool(communicator, sequence)
                {
                    LogProgress = new Progress<string>(Log)
                };

                try
                {
                    return await bootloaderTool.StartBootloaderAsync(token).ConfigureAwait(true);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Log($"'{name}' did not work, trying next candidate if any...");
                    lastError = ex;
                }
            }

            throw lastError;
        }

        private void SetBusy(bool busy)
        {
            UploadButton.IsEnabled = !busy;
            CancelButton.IsEnabled = busy;
            BrowseButton.IsEnabled = !busy;
            PortComboBox.IsEnabled = !busy;
            RefreshPortsButton.IsEnabled = !busy;
            OffsetTextBox.IsEnabled = !busy;
            if (!busy)
                UploadProgressBar.Value = 0;
        }

        private void Log(string message)
        {
            // Raw banners from the device can contain \r\n - escape them so each log entry stays
            // on one line and keeps its timestamp prefix readable.
            string sanitized = message.Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "\\n");
            LogTextBox.AppendText($"{DateTime.Now:HH:mm:ss} {sanitized}{Environment.NewLine}");
            LogTextBox.ScrollToEnd();
        }
    }
}
