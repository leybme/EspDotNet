using EspDotNet.Exceptions;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EspDotNet.Config
{
    public static class ConfigProvider
    {
        private static readonly string EmbeddedResourcePrefix = $"{Assembly.GetExecutingAssembly().GetName().Name}.Resources.Config.";
        private static readonly string DevicesFolder = "Devices.";

        /// <summary>
        /// Loads the default ESPToolConfig from embedded resources.
        /// </summary>
        public static ESPToolConfig LoadDefaultConfig()
        {
            string resourceName = $"{EmbeddedResourcePrefix}ESPToolConfig.json";

            try
            {
                string json = ReadEmbeddedJson(resourceName);
                var config = JsonSerializer.Deserialize<ESPToolConfig>(json) ?? new ESPToolConfig();
                config.Devices = LoadDefaultDevices();

                return config;
            }
            catch (Exception ex)
            {
                // The default config is embedded in this assembly; a failure here indicates a
                // packaging/build problem rather than a recoverable runtime condition.
                throw new EspException($"Failed to load the embedded default ESPToolConfig ('{resourceName}').", ex);
            }
        }

        /// <summary>
        /// Loads all embedded device configurations.
        /// </summary>
        private static List<DeviceConfig> LoadDefaultDevices()
        {
            var devices = new List<DeviceConfig>();

            foreach (var resourceName in GetEmbeddedDeviceConfigNames())
            {
                try
                {
                    devices.Add(LoadDeviceConfig(resourceName));
                }
                catch (Exception ex)
                {
                    // A single malformed device file should not prevent the rest from loading.
                    Debug.WriteLine($"Failed to load device config '{resourceName}': {ex.Message}");
                }
            }

            return devices;
        }

        /// <summary>
        /// Loads a single device configuration from an embedded JSON file.
        /// </summary>
        private static DeviceConfig LoadDeviceConfig(string resourceName)
        {
            var options = new JsonSerializerOptions
            {
                Converters =
                {
                    new JsonStringEnumConverter()
                }
            };
            string json = ReadEmbeddedJson(resourceName);
            return JsonSerializer.Deserialize<DeviceConfig>(json, options)
                ?? throw new Exception($"Failed to deserialize JSON from resource: {resourceName}");
        }

        /// <summary>
        /// Reads JSON data from an embedded resource.
        /// </summary>
        private static string ReadEmbeddedJson(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();

            using Stream stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new Exception($"Embedded resource '{resourceName}' not found in assembly.");

            using StreamReader reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        /// <summary>
        /// Finds all embedded JSON files for devices.
        /// </summary>
        private static IEnumerable<string> GetEmbeddedDeviceConfigNames()
        {
            var assembly = Assembly.GetExecutingAssembly();
            return assembly.GetManifestResourceNames()
                .Where(name => name.StartsWith(EmbeddedResourcePrefix + DevicesFolder) && name.EndsWith(".json"));
        }
    }
}




