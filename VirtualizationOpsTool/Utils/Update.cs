// ============================================================================
// Update.cs - 远程版本检查与下载 + 启动 UpdateHelper
// ============================================================================
// 职责：
//   1. HttpClient 请求 Gitee Release API，比对本地 AppVersion.VERSION_STR
//   2. 从 latest Release 的 assets 里找到 .exe 附件，下载到临时目录（带进度）
//   3. 每次都重新从嵌入资源释放 update_helper.exe（覆盖旧文件）
//   4. 启动 helper 时传入主进程 PID，主程序用 Environment.Exit 强制退出
// Gitee Release API（无需 token，公开访问）：
//   https://gitee.com/api/v5/repos/hack003/VmOps-Tool/releases/latest
// ============================================================================
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VirtualizationOpsTool.Utils
{
    public static class UpdateUtils
    {
        public const string RELEASE_API_URL = "https://gitee.com/api/v5/repos/hack003/VmOps-Tool/releases/latest";

        private const string EmbeddedHelperResourceName = "VirtualizationOpsTool.Resources.update_helper.exe";

        private static readonly object _helperLock = new object();

        [Conditional("DEBUG")]
        private static void Log(string msg) => Debug.WriteLine($"[Update] {msg}");

        public static async Task<UpdateInfo> CheckForUpdatesAsync(string currentVersion)
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(15);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("VmOps-Update-Checker/1.0");

                var response = await client.GetAsync(RELEASE_API_URL);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync();
                var obj = JObject.Parse(json);

                var tagName = (string?)obj["tag_name"] ?? "";
                var version = tagName.TrimStart('v', 'V').Trim();

                var downloadUrl = (string?)obj["html_url"] ?? "";
                var changelog = (string?)obj["body"] ?? "";

                var packageUrl = "";
                var assets = obj["assets"] as JArray;
                if (assets != null)
                {
                    var exeAsset = assets.FirstOrDefault(a =>
                    {
                        var n = (string?)a["name"] ?? "";
                        return n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
                    });
                    if (exeAsset != null)
                        packageUrl = (string?)exeAsset["browser_download_url"] ?? "";
                }

                if (string.IsNullOrEmpty(version)) return null;

                var info = new UpdateInfo
                {
                    Version = version,
                    DownloadUrl = downloadUrl,
                    PackageUrl = packageUrl,
                    Changelog = changelog,
                    IsNewer = IsNewerVersion(version, currentVersion)
                };
                return info;
            }
            catch (Exception ex)
            {
                Log($"CheckForUpdatesAsync 异常: {ex.Message}");
                return null;
            }
        }

        public static async Task<bool> DownloadPackageAsync(UpdateInfo info, string destPath, IProgress<double>? progress = null)
        {
            if (info == null || string.IsNullOrEmpty(info.PackageUrl)) return false;

            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromMinutes(5);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("VmOps-Update-Downloader/1.0");

                using var response = await client.GetAsync(info.PackageUrl, HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode) return false;

                long totalBytes = response.Content.Headers.ContentLength ?? -1;
                long downloadedBytes = 0;

                using var stream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
                var buffer = new byte[8192];
                int bytesRead;
                double lastProgress = -1;

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    downloadedBytes += bytesRead;

                    if (totalBytes > 0 && progress != null)
                    {
                        double pct = Math.Round(downloadedBytes * 100.0 / totalBytes, 0);
                        if (pct - lastProgress >= 1)
                        {
                            progress.Report(pct);
                            lastProgress = pct;
                        }
                    }
                    else if (progress != null && (downloadedBytes % (1024 * 1024)) < 8192)
                    {
                        progress.Report(-1);
                    }
                }

                progress?.Report(100);
                return true;
            }
            catch (Exception ex)
            {
                Log($"DownloadPackageAsync 异常: {ex.Message}");
                return false;
            }
        }

        public static bool RunUpdateHelper(string newExePath)
        {
            try
            {
                string? helperPath = ReleaseHelper();
                if (helperPath == null || !File.Exists(helperPath))
                {
                    Log("无法释放 update_helper.exe");
                    return false;
                }

                string targetExe = Process.GetCurrentProcess().MainModule!.FileName;
                int currentPid = Process.GetCurrentProcess().Id;

                Log($"启动 helper: target={targetExe}, pid={currentPid}, new={newExePath}");

                Process.Start(new ProcessStartInfo
                {
                    FileName = helperPath,
                    Arguments = $"\"{targetExe}\" \"{newExePath}\" --pid={currentPid}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                return true;
            }
            catch (Exception ex)
            {
                Log($"RunUpdateHelper 异常: {ex.Message}");
                return false;
            }
        }

        private static string? ReleaseHelper()
        {
            lock (_helperLock)
            {
                try
                {
                    string tempDir = Path.Combine(Path.GetTempPath(), "VmOps_Update");
                    Directory.CreateDirectory(tempDir);

                    string helperPath = Path.Combine(tempDir, "update_helper.exe");

                    var asm = typeof(UpdateUtils).Assembly;
                    string? resourceName = null;

                    foreach (var name in asm.GetManifestResourceNames())
                    {
                        if (name.Equals(EmbeddedHelperResourceName, StringComparison.OrdinalIgnoreCase))
                        {
                            resourceName = name;
                            break;
                        }
                    }
                    if (resourceName == null)
                    {
                        resourceName = asm.GetManifestResourceNames()
                            .FirstOrDefault(n => n.EndsWith("update_helper.exe", StringComparison.OrdinalIgnoreCase));
                    }

                    if (resourceName == null)
                    {
                        Log("未找到 update_helper.exe 嵌入资源");
                        return null;
                    }

                    using var stream = asm.GetManifestResourceStream(resourceName);
                    if (stream == null) return null;

                    using var fs = new FileStream(helperPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    stream.CopyTo(fs);

                    Log($"已释放 helper 到: {helperPath}");
                    return helperPath;
                }
                catch (Exception ex)
                {
                    Log($"ReleaseHelper 异常: {ex.Message}");
                    return null;
                }
            }
        }

        private static bool IsNewerVersion(string newVersion, string currentVersion)
        {
            try
            {
                var cleanNew = SanitizeVersion(newVersion);
                var cleanCurrent = SanitizeVersion(currentVersion);

                var newParts = cleanNew.Split('.');
                var currentParts = cleanCurrent.Split('.');

                for (int i = 0; i < Math.Max(newParts.Length, currentParts.Length); i++)
                {
                    int newPart = i < newParts.Length && int.TryParse(newParts[i], out var np) ? np : 0;
                    int currentPart = i < currentParts.Length && int.TryParse(currentParts[i], out var cp) ? cp : 0;

                    if (newPart > currentPart) return true;
                    if (newPart < currentPart) return false;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static string SanitizeVersion(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return "0";
            v = v.Trim().TrimStart('v', 'V');

            int dash = v.IndexOf('-');
            if (dash >= 0) v = v.Substring(0, dash);

            var parts = v.Split('.');
            var clean = new System.Collections.Generic.List<string>();
            foreach (var p in parts)
            {
                var numeric = Regex.Replace(p, "[^0-9]", "");
                if (!string.IsNullOrEmpty(numeric))
                    clean.Add(numeric);
            }
            if (clean.Count == 0) clean.Add("0");

            while (clean.Count < 4) clean.Add("0");
            if (clean.Count > 4) clean = clean.Take(4).ToList();

            return string.Join(".", clean);
        }

        public class UpdateInfo
        {
            public string Version { get; set; } = "";

            public string DownloadUrl { get; set; } = "";

            public string PackageUrl { get; set; } = "";

            public string Changelog { get; set; } = "";

            [JsonIgnore]
            public bool IsNewer { get; set; }
        }
    }
}