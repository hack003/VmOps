using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using VMwareOps.Utils;
using Newtonsoft.Json;

namespace VMwareOps.Core
{
    public class ExtensionManager
    {
        public const string DefaultRegistryUrl = "https://gitee.com/hack003/vmops-webserver/raw/master/registry.json";

        private static readonly Lazy<ExtensionManager> _instance = new Lazy<ExtensionManager>(() => new ExtensionManager());
        public static ExtensionManager Instance => _instance.Value;

        private readonly string _extensionsJsonPath;
        private readonly string _extensionsDir;
        private LocalRegistry _registry;
        private bool _initialized;
        private List<RemoteExtension>? _cachedMarket;
        private DateTime _cacheTime = DateTime.MinValue;

        public event Action<string, string>? Log;

        private ExtensionManager()
        {
            _extensionsDir = CoreUtils.GetExtensionsDir();
            _extensionsJsonPath = Path.Combine(CoreUtils.GetUserDataDir(), "extensions.json");
            _registry = new LocalRegistry { Extensions = new List<InstalledExtension>() };
        }

        public void Initialize()
        {
            if (_initialized) return;

            try
            {
                LoadRegistry();
                ScanInstalledExtensions();
                _initialized = true;
                Log?.Invoke($"扩展管理器已初始化，共 {_registry.Extensions.Count(e => e.Enabled)} 个启用 / {_registry.Extensions.Count} 个已安装", "info");
            }
            catch (Exception ex)
            {
                Log?.Invoke($"扩展管理器初始化失败: {ex.Message}", "warn");
            }
        }

        private void LoadRegistry()
        {
            if (!File.Exists(_extensionsJsonPath))
            {
                SaveRegistry();
                return;
            }

            try
            {
                var json = File.ReadAllText(_extensionsJsonPath, Encoding.UTF8);
                _registry = JsonConvert.DeserializeObject<LocalRegistry>(json) ?? new LocalRegistry();
                _registry.Extensions ??= new List<InstalledExtension>();
            }
            catch
            {
                _registry = new LocalRegistry { Extensions = new List<InstalledExtension>() };
            }
        }

        private void SaveRegistry()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_registry, Formatting.Indented);
                File.WriteAllText(_extensionsJsonPath, json, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Log?.Invoke($"保存扩展注册表失败: {ex.Message}", "warn");
            }
        }

        private void ScanInstalledExtensions()
        {
            if (!Directory.Exists(_extensionsDir)) return;

            var installedIds = new HashSet<string>();
            foreach (var dir in Directory.GetDirectories(_extensionsDir))
            {
                var id = Path.GetFileName(dir);
                var manifestPath = Path.Combine(dir, "extension.json");

                if (!File.Exists(manifestPath)) continue;

                try
                {
                    var manifestJson = File.ReadAllText(manifestPath, Encoding.UTF8);
                    var manifest = JsonConvert.DeserializeObject<ExtensionManifest>(manifestJson);
                    if (manifest == null || string.IsNullOrEmpty(manifest.Id)) continue;

                    installedIds.Add(manifest.Id);

                    var existing = _registry.Extensions.FirstOrDefault(e => e.Id == manifest.Id);
                    if (existing == null)
                    {
                        _registry.Extensions.Add(new InstalledExtension
                        {
                            Id = manifest.Id,
                            Name = manifest.Name,
                            Version = manifest.Version,
                            Enabled = false,
                            InstalledAt = DateTime.Now,
                            SourceUrl = manifest.SourceUrl
                        });
                    }
                    else
                    {
                        existing.Name = manifest.Name;
                        existing.Version = manifest.Version;
                    }
                }
                catch (Exception ex)
                {
                    Log?.Invoke($"扫描扩展 {id} 失败: {ex.Message}", "warn");
                }
            }

            var removed = _registry.Extensions.Where(e => !installedIds.Contains(e.Id)).ToList();
            foreach (var r in removed)
            {
                Log?.Invoke($"扩展 {r.Id} 文件已丢失，从注册表移除", "warn");
                _registry.Extensions.Remove(r);
            }

            SaveRegistry();
        }

        public List<InstalledExtension> GetInstalled() => _registry.Extensions.ToList();

        public List<InstalledExtension> GetEnabled() => _registry.Extensions.Where(e => e.Enabled).ToList();

        public InstalledExtension GetExtension(string id) =>
            _registry.Extensions.FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

        public bool SetEnabled(string id, bool enabled)
        {
            var ext = GetExtension(id);
            if (ext == null) return false;

            ext.Enabled = enabled;
            SaveRegistry();
            Log?.Invoke($"扩展 {id} {(enabled ? "已启用" : "已禁用")}", "info");
            return true;
        }

        public async Task<InstallResult> InstallAsync(string id, string packageUrl)
        {
            try
            {
                Log?.Invoke($"开始下载扩展 {id}...", "info");

                var tempDir = Path.Combine(Path.GetTempPath(), "VmOps_Extensions", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);

                var zipPath = Path.Combine(tempDir, $"{id}.zip");

                using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
                {
                    using (var response = await client.GetAsync(packageUrl))
                    {
                        if (!response.IsSuccessStatusCode)
                            return InstallResult.Fail($"下载失败: HTTP {(int)response.StatusCode}");

                        using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
                        {
                            await response.Content.ReadAsStreamAsync().ContinueWith(s => s.Result.CopyToAsync(fs)).Unwrap();
                        }
                    }
                }

                Log?.Invoke($"下载完成，开始解压...", "info");

                var extractDir = Path.Combine(tempDir, "extract");
                Directory.CreateDirectory(extractDir);
                ZipFile.ExtractToDirectory(zipPath, extractDir);

                var manifestPath = FindFile(extractDir, "extension.json");
                if (manifestPath == null)
                    return InstallResult.Fail("扩展包缺少 extension.json 清单");

                var manifestJson = File.ReadAllText(manifestPath, Encoding.UTF8);
                var manifest = JsonConvert.DeserializeObject<ExtensionManifest>(manifestJson);
                if (manifest == null || string.IsNullOrEmpty(manifest.Id))
                    return InstallResult.Fail("extension.json 格式无效");

                if (manifest.Id != id)
                    return InstallResult.Fail($"扩展 ID 不匹配（期望 {id}，实际 {manifest.Id}）");

                var targetDir = Path.Combine(_extensionsDir, manifest.Id);
                if (Directory.Exists(targetDir))
                {
                    Directory.Delete(targetDir, true);
                }

                var manifestDir = Path.GetDirectoryName(manifestPath);
                Directory.Move(manifestDir!, targetDir);

                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }

                var existing = _registry.Extensions.FirstOrDefault(e => e.Id == manifest.Id);
                if (existing != null)
                {
                    existing.Name = manifest.Name;
                    existing.Version = manifest.Version;
                    existing.SourceUrl = packageUrl;
                }
                else
                {
                    _registry.Extensions.Add(new InstalledExtension
                    {
                        Id = manifest.Id,
                        Name = manifest.Name,
                        Version = manifest.Version,
                        Enabled = true,
                        InstalledAt = DateTime.Now,
                        SourceUrl = packageUrl
                    });
                }
                SaveRegistry();

                Log?.Invoke($"扩展 {manifest.Name} v{manifest.Version} 安装成功", "success");
                return InstallResult.Ok(manifest);
            }
            catch (Exception ex)
            {
                Log?.Invoke($"安装扩展 {id} 失败: {ex.Message}", "error");
                return InstallResult.Fail(ex.Message);
            }
        }

        public bool Uninstall(string id)
        {
            var ext = GetExtension(id);
            if (ext == null) return false;

            var targetDir = Path.Combine(_extensionsDir, id);
            try
            {
                if (Directory.Exists(targetDir))
                {
                    Directory.Delete(targetDir, true);
                }

                _registry.Extensions.Remove(ext);
                SaveRegistry();
                Log?.Invoke($"扩展 {id} 已卸载", "info");
                return true;
            }
            catch (Exception ex)
            {
                Log?.Invoke($"卸载扩展 {id} 失败: {ex.Message}", "error");
                return false;
            }
        }

        public async Task<List<RemoteExtension>> FetchRegistryAsync(string registryUrl)
        {
            try
            {
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) })
                {
                    var json = await client.GetStringAsync(registryUrl);
                    var result = JsonConvert.DeserializeObject<RemoteRegistry>(json);
                    return result?.Extensions ?? new List<RemoteExtension>();
                }
            }
            catch (Exception ex)
            {
                Log?.Invoke($"获取扩展市场失败: {ex.Message}", "warn");
                return new List<RemoteExtension>();
            }
        }

        public async Task<List<RemoteExtension>> FetchMarketAsync(bool forceRefresh = false)
        {
            if (!forceRefresh && _cachedMarket != null && (DateTime.Now - _cacheTime).TotalMinutes < 10)
            {
                return _cachedMarket;
            }

            _cachedMarket = await FetchRegistryAsync(DefaultRegistryUrl);
            _cacheTime = DateTime.Now;
            return _cachedMarket;
        }

        public async Task<InstallResult> InstallFromMarketAsync(RemoteExtension remote)
        {
            if (string.IsNullOrEmpty(remote.PackageUrl))
            {
                return InstallResult.Fail("扩展没有下载地址");
            }
            return await InstallAsync(remote.Id, remote.PackageUrl);
        }

        private static string? FindFile(string rootDir, string fileName)
        {
            try
            {
                foreach (var file in Directory.GetFiles(rootDir, fileName, SearchOption.AllDirectories))
                {
                    return file;
                }
            }
            catch { }
            return null;
        }

        public string GetExtensionDir(string id) => Path.Combine(_extensionsDir, id);
    }

    public class LocalRegistry
    {
        public List<InstalledExtension> Extensions { get; set; } = new List<InstalledExtension>();
    }

    public class InstalledExtension
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public bool Enabled { get; set; }
        public DateTime InstalledAt { get; set; }
        public string? SourceUrl { get; set; }
    }

    public class ExtensionManifest
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public string? Author { get; set; }
        public string? Description { get; set; }
        public string? MinAppVersion { get; set; }
        public string? SourceUrl { get; set; }
        public string? Wwwroot { get; set; }
        public string? EntryRoute { get; set; }
    }

    public class RemoteRegistry
    {
        public List<RemoteExtension> Extensions { get; set; } = new List<RemoteExtension>();
    }

    public class RemoteExtension
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
        public string? Description { get; set; }
        public string? PackageUrl { get; set; }
        public string? MinAppVersion { get; set; }
    }

    public class InstallResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public ExtensionManifest? Manifest { get; set; }

        public static InstallResult Ok(ExtensionManifest m) => new InstallResult { Success = true, Manifest = m };
        public static InstallResult Fail(string err) => new InstallResult { Success = false, Error = err };
    }
}