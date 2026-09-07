using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace BossKey
{
    [DataContract]
    internal sealed class HotkeyConfig
    {
        [DataMember]
        public uint Modifiers { get; set; }

        [DataMember]
        public uint VirtualKey { get; set; }

        public HotkeyConfig()
        {
        }

        public HotkeyConfig(uint modifiers, uint virtualKey)
        {
            Modifiers = modifiers;
            VirtualKey = virtualKey;
        }
    }

    [DataContract]
    internal sealed class AppConfig
    {
        public AppConfig()
        {
            TargetExePaths = new List<string>();
            HideHotkey = new HotkeyConfig(0x0002, 0xC0);
            ShowHotkey = new HotkeyConfig(0x0006, 0xC0);
            MouseMiddle = true;
            MouseX1 = false;
            MouseX2 = false;
            RunAtStartup = false;
        }

        [DataMember]
        public List<string> TargetExePaths { get; set; }

        [DataMember]
        public HotkeyConfig HideHotkey { get; set; }

        [DataMember]
        public HotkeyConfig ShowHotkey { get; set; }

        [DataMember]
        public bool MouseMiddle { get; set; }

        [DataMember]
        public bool MouseX1 { get; set; }

        [DataMember]
        public bool MouseX2 { get; set; }

        [DataMember]
        public bool RunAtStartup { get; set; }
    }

    internal static class ConfigStore
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "boss-key");

        private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

        public static string GetConfigFilePath()
        {
            return ConfigPath;
        }

        public static AppConfig Load()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    return new AppConfig();
                }

                using (var stream = File.OpenRead(ConfigPath))
                {
                    var serializer = new DataContractJsonSerializer(typeof(AppConfig));
                    return serializer.ReadObject(stream) as AppConfig ?? new AppConfig();
                }
            }
            catch
            {
                return new AppConfig();
            }
        }

        public static void Save(AppConfig config)
        {
            Directory.CreateDirectory(ConfigDir);
            using (var stream = File.Create(ConfigPath))
            {
                var serializer = new DataContractJsonSerializer(typeof(AppConfig));
                serializer.WriteObject(stream, config);
            }
        }

        public static string NormalizeExePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            return Path.GetFullPath(path).Trim().ToLowerInvariant();
        }

        public static string GetDisplayName(string exePath)
        {
            try
            {
                return Path.GetFileNameWithoutExtension(exePath) ?? exePath;
            }
            catch
            {
                return exePath;
            }
        }
    }
}
