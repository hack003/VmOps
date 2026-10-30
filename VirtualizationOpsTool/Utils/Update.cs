// ============================================================================
// Update.cs - 远程版本检查与下载
// ============================================================================
// 职责：
//   1. HttpClient 请求 Gitee Release API，比对本地 AppVersion.VERSION_STR
//   2. 从 latest Release 的 assets 里找到 .exe 附件，下载到临时目录（带进度）
//   3. 从嵌入资源释放 update_helper.exe，启动执行替换
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

        private static string? _releasedHelperPath;

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
                var version = tagName.TrimStart('v', 'V');

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
            catch
            {
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
            catch
            {
                return false;
            }
        }

        public static bool RunUpdateHelper(string newExePath)
        {
            try
            {
                string? helperPath = EnsureHelperReleased();
                if (helperPath == null || !File.Exists(helperPath)) return false;

                string targetExe = Process.GetCurrentProcess().MainModule!.FileName;

                Process.Start(new ProcessStartInfo
                {
                    FileName = helperPath,
                    Arguments = $"\"{targetExe}\" \"{newExePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string? EnsureHelperReleased()
        {
            if (_releasedHelperPath != null && File.Exists(_releasedHelperPath))
                return _releasedHelperPath;

            string tempDir = Path.Combine(Path.GetTempPath(), "VmOps_Update");
            Directory.CreateDirectory(tempDir);

            string helperPath = Path.Combine(tempDir, "update_helper.exe");

            if (File.Exists(helperPath))
            {
                _releasedHelperPath = helperPath;
                return helperPath;
            }

            try
            {
                var asm = typeof(UpdateUtils).Assembly;
                using var stream = asm.GetManifestResourceStream(EmbeddedHelperResourceName);
                if (stream == null)
                {
                    foreach (var name in asm.GetManifestResourceNames())
                    {
                        if (name.EndsWith("update_helper.exe"))
                        {
                            using var fallbackStream = asm.GetManifestResourceStream(name)!;
                            using var fs = new FileStream(helperPath, FileMode.Create, FileAccess.Write);
                            fallbackStream.CopyTo(fs);
                            _releasedHelperPath = helperPath;
                            return helperPath;
                        }
                    }
                    return null;
                }
                using (var fs = new FileStream(helperPath, FileMode.Create, FileAccess.Write))
                {
                    stream.CopyTo(fs);
                }
                _releasedHelperPath = helperPath;
                return helperPath;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsNewerVersion(string newVersion, string currentVersion)
        {
            try
            {
                var newParts = newVersion.Split('.');
                var currentParts = currentVersion.Split('.');

                for (int i = 0; i < Math.Max(newParts.Length, currentParts.Length); i++)
                {
                    int newPart = i < newParts.Length ? int.Parse(Regex.Replace(newParts[i], "[^0-9]", "")) : 0;
                    int currentPart = i < currentParts.Length ? int.Parse(Regex.Replace(currentParts[i], "[^0-9]", "")) : 0;

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