# EspDotNet - ESP32 Flashing and Bootloader Tool

**EspDotNet** is a native C# implementation of Espressif's ESP tool ([esptool](https://github.com/espressif/esptool)). This library was created to enable direct interaction with ESP devices (such as ESP32) without relying on external applications. It provides a rich set of tools for flashing firmware, erasing flash memory, detecting chip types, and managing bootloader and softloader communication—all via serial communication.

[![NuGet](https://img.shields.io/nuget/v/EspDotNet.svg)](https://www.nuget.org/packages/EspDotNet)

Targets `net8.0` and `net472`, so it can be referenced from both modern .NET apps and .NET Framework apps (e.g. WinForms/WPF on `net472`).

> **Looking for a GUI tool?** Check out the [ESPFlasher GUI tool on GitHub](https://github.com/KooleControls/ESPFlasher), or the minimal [EspFlasherWpf sample](./Samples/EspFlasherWpf) in this repo.

## Features

- **Bootloader and Softloader Communication:**  
  - Execute pin sequences to start the bootloader.
  - Load and run a softloader.
- **Firmware Operations:**  
  - Upload firmware using dedicated tools (Flash, Flash Deflated, RAM).
  - Erase flash memory.
- **Device Management:**  
  - Detect chip type.
  - Reset the device.

## Supported Devices

While the code is designed to support all ESP devices, testing has only been performed on a subset. Devices tested include:

- [ ] ESP8266  
- [x] ESP32  
- [ ] ESP32-c2  
- [x] ESP32-c3  
- [ ] ESP32-c6  
- [ ] ESP32-h2  
- [ ] ESP32-p4  
- [ ] ESP32-s2  
- [x] ESP32-s3  
- [ ] ESP32-c6beta  
- [ ] ESP32-h2beta1  
- [ ] ESP32-h2beta2  
- [ ] ESP32-s3beta2  

## Architecture Overview

The library is organized into several self‑contained tools, each responsible for a specific aspect of ESP device communication and firmware handling:

### Loader Tools

- **`BootloaderTool`**  
  Initiates communication with the built‑in ROM bootloader on the ESP device.
- **`SoftloaderTool`**  
  Handles the process of uploading a softloader (stubloader) into RAM, which extends functionality with commands not available in the ROM bootloader.

### Firmware Upload Tools

Each upload tool encapsulates a specific firmware upload mechanism:
- **`UploadRamTool`** – For uploading firmware directly into RAM.
- **`UploadFlashTool`** – For flashing firmware to the device’s flash memory.
- **`UploadFlashDeflatedTool`** – For flashing compressed (deflated) firmware images.
- **`FirmwareUploadTool`** – Wraps around an upload tool and manages firmware segmented transfers with progress reporting.

### Additional Tools

- **`ChipTypeDetectTool`** – Detects the chip type of the connected device.
- **`ChangeBaudrateTool`** – Handles baud rate changes.
- **`FlashEraseTool`** – Erases the device’s flash memory.
- **`ResetDeviceTool`** – Resets the device by executing a reset pin sequence.

## Example

Each tool is a small, self-contained class you construct directly. The only shared piece of state is the `ESPToolConfig` (device definitions and the bootloader/reset pin sequences), which you load once from the bundled defaults via `ConfigProvider.LoadDefaultConfig()`. The design forces you to explicitly handle the device state by passing the appropriate loader and chip type when needed, reducing the risk of operating on a disconnected or unsupported device.

The following walks through detecting a chip, loading the stub (softloader) and flashing a firmware image:

```csharp
using EspDotNet.Communication;
using EspDotNet.Config;
using EspDotNet.Tools;
using EspDotNet.Tools.Firmware;
using System.IO.Ports;

// Load the bundled device + pin-sequence config.
var config = ConfigProvider.LoadDefaultConfig();

// 1. Open the serial port and wrap it in a communicator (the port is not owned by the library).
using var port = new SerialPort("COM3", 115200);
port.Open();
var communicator = new Communicator(port);

// 2. Enter the ROM bootloader and synchronize (needs the bootloader pin sequence).
var bootloaderTool = new BootloaderTool(communicator, config.BootloaderSequence);
var bootloader = await bootloaderTool.StartBootloaderAsync();

// 3. Detect the chip and resolve its device config.
var chipDetectTool = new ChipTypeDetectTool(bootloader, config);
var deviceConfig = await chipDetectTool.DetectAndGetDeviceConfigAsync(default);

// 4. Upload the matching stub loader into RAM and run it (the softloader adds extra commands).
var stub = DefaultFirmwareProviders.GetSoftloaderForDevice(deviceConfig.ChipType);
var ramUploadTool = new RamUploadTool(bootloader, deviceConfig);
var softLoaderTool = new SoftLoaderTool(communicator, ramUploadTool);
var softLoader = await softLoaderTool.StartAsync(stub);

// 5. Flash your firmware (provide your own IFirmwareProvider) using the compressed flash path.
IFirmwareProvider firmware = /* your firmware image */;

// FirmwareUploadTool drives the segment/progress loop; it delegates the actual writing to an
// IUploadTool - here the deflated (compressed) flash uploader.
var deflatedUploadTool = new FlashUploadDeflatedTool(softLoader, deviceConfig);
var firmwareUploadTool = new FirmwareUploadTool(deflatedUploadTool)
{
    Progress = new Progress<float>(p => Console.WriteLine($"{p:P0}"))
};
await firmwareUploadTool.UploadFirmwareAsync(firmware, default);

// 6. Reset the device to run the new firmware (needs the reset pin sequence).
var resetTool = new ResetDeviceTool(communicator, config.ResetSequence);
await resetTool.ResetAsync();
```

## Samples

- **[EspFlasherWpf](./Samples/EspFlasherWpf)** – a small `net472` WPF app that flashes a `firmware.bin` to an ESP32-C3/S3 board: pick a COM port, pick the `.bin`, set the flash offset (defaults to `0x10000`), and watch progress/log output.

  Boards using the ESP32-C3/S3's built-in USB-Serial-JTAG peripheral (no external CP2102/CH340 bridge) need a different DTR/RTS reset sequence than the classic external-bridge auto-reset circuit – the peripheral latches "enter download mode" from the DTR/RTS edge sequence itself, rather than sampling a GPIO9 strap pin at the moment reset is released. The sample's [`BootloaderResetSequences.cs`](./Samples/EspFlasherWpf/BootloaderResetSequences.cs) tries a few known sequences in turn; see the comments there if your board needs yet another variant.

## License

This project is licensed under the MIT License. See [LICENSE](./LICENSE) for details.

## Additional Resources

- **Official ESPTool Protocol Documentation:** [Espressif Docs](https://docs.espressif.com/projects/esptool/en/latest/esp32/advanced-topics/serial-protocol.html)
- **Stubloaders Location:**  
  Typically found in the toolchain under:  
  `\tools\python_env\idf5.0_py3.11_env\Lib\site-packages\esptool\targets\stub_flasher`

