// ============================================================================
// AppSettings.cs - 全局应用设置（JSON 文件持久化）
// ============================================================================
// 存储位置：%AppData%\VmOps\settings.json
// 单例访问 AppSettings.Instance
//
// 路径类字段：null 或空字符串 = 自动探测（沿用现有 Find* 逻辑）
//             有值 = 强制使用该路径（不校验文件存在性，调用方按需判断）
// ============================================================================
using System;
using System.IO;
using Newtonsoft.Json;

namespace VmOps.Utils
{
    public class AppSettings
    {
        private static readonly string SettingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VmOps");

        private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");

        private static AppSettings? _instance;
        private static readonly object _lock = new object();

        public static AppSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= Load();
                    }
                }
                return _instance;
            }
        }

        public bool MinimizeToTrayOnStart { get; set; } = false;

        public bool MinimizeToTrayOnClose { get; set; } = true;

        public bool SingleInstanceMode { get; set; } = true;

        public string? OvfToolPath { get; set; }

        public string? VmrcPath { get; set; }

        public string? RdpPath { get; set; }

        public string? BrowserPath { get; set; }

        public string? MobaXtermPath { get; set; }

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var json = File.ReadAllText(SettingsFile);
                    var settings = JsonConvert.DeserializeObject<AppSettings>(json);
                    if (settings != null) return settings;
                }
            }
            catch { }

            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                if (!Directory.Exists(SettingsDir))
                    Directory.CreateDirectory(SettingsDir);

                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(SettingsFile, json);
            }
            catch { }
        }

        private static readonly string[] OvfToolDefaults =
        {
            @"C:\Program Files (x86)\VMware\VMware Workstation\OVFTool\ovftool.exe",
            @"C:\Program Files\VMware\VMware OVF Tool\ovftool.exe"
        };
        private const string VmrcDefault       = @"C:\Program Files (x86)\VMware\VMware Remote Console\vmrc.exe";
        private const string RdpDefault        = @"C:\Windows\System32\mstsc.exe";
        private const string BrowserDefault    = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
        private const string MobaXtermDefault  = @"C:\Program Files (x86)\Mobatek\MobaXterm\MobaXterm.exe";

        public string? GetOvfToolPath()    => Resolve(OvfToolPath, OvfToolDefaults);
        public string? GetVmrcPath()       => Resolve(VmrcPath, VmrcDefault);
        public string? GetRdpPath()        => Resolve(RdpPath, RdpDefault);
        public string? GetBrowserPath()    => Resolve(BrowserPath, BrowserDefault);
        public string? GetMobaXtermPath()  => Resolve(MobaXtermPath, MobaXtermDefault);

        private static string? Resolve(string? userPath, string defaultValue)
        {
            if (!string.IsNullOrWhiteSpace(userPath) && File.Exists(userPath))
                return userPath;
            if (File.Exists(defaultValue))
                return defaultValue;
            return null;
        }

        private static string? Resolve(string? userPath, string[] defaultValues)
        {
            if (!string.IsNullOrWhiteSpace(userPath) && File.Exists(userPath))
                return userPath;
            foreach (var candidate in defaultValues)
            {
                if (File.Exists(candidate))
                    return candidate;
            }
            return null;
        }

        public static string GetSettingsPath() => SettingsFile;
    }
}