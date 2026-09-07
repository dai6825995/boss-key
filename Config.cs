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
            TopmostHotkey = new HotkeyConfig(0x0003, 0x50);
            OpacityDownHotkey = new HotkeyConfig(0x0003, 0x28);
            OpacityUpHotkey = new HotkeyConfig(0x0003, 0x26);
            OpacityRestoreHotkey = new HotkeyConfig(0x0003, 0x52);
            SettingsHotkey = new HotkeyConfig(0x0006, 0x39);
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

        [DataMember]
        public HotkeyConfig TopmostHotkey { get; set; }

        [DataMember]
        public HotkeyConfig OpacityDownHotkey { get; set; }

        [DataMember]
        public HotkeyConfig OpacityUpHotkey { get; set; }

        [DataMember]
        public HotkeyConfig OpacityRestoreHotkey { get; set; }

        [DataMember]
        public HotkeyConfig SettingsHotkey { get; set; }
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
                    var loaded = serializer.ReadObject(stream) as AppConfig ?? new AppConfig();
                    if (loaded.TargetExePaths == null)
                    {
                        loaded.TargetExePaths = new List<string>();
                    }
                    if (loaded.HideHotkey == null)
                    {
                        loaded.HideHotkey = new HotkeyConfig(0x0002, 0xC0);
                    }
                    if (loaded.ShowHotkey == null)
                    {
                        loaded.ShowHotkey = new HotkeyConfig(0x0006, 0xC0);
                    }
                    if (loaded.TopmostHotkey == null)
                    {
                        loaded.TopmostHotkey = new HotkeyConfig(0x0003, 0x50);
                    }
                    if (loaded.OpacityDownHotkey == null)
                    {
                        loaded.OpacityDownHotkey = new HotkeyConfig(0x0003, 0x28);
                    }
                    if (loaded.OpacityUpHotkey == null)
                    {
                        loaded.OpacityUpHotkey = new HotkeyConfig(0x0003, 0x26);
                    }
                    if (loaded.OpacityRestoreHotkey == null)
                    {
                        loaded.OpacityRestoreHotkey = new HotkeyConfig(0x0003, 0x52);
                    }
                    if (loaded.SettingsHotkey == null)
                    {
                        loaded.SettingsHotkey = new HotkeyConfig(0x0006, 0x39);
                    }
                    return loaded;
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

        public static bool IsProtectedExe(string exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath))
            {
                return false;
            }

            var name = Path.GetFileName(exePath);
            return string.Equals(name, "explorer.exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "dwm.exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "winlogon.exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "csrss.exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "sihost.exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "searchhost.exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "startmenuexperiencehost.exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "shellexperiencehost.exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "textinputhost.exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "runtimebroker.exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "taskmgr.exe", StringComparison.OrdinalIgnoreCase);
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
