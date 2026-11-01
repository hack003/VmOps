// ============================================================================
// UnifiedCache.cs - 统一资源缓存管理
// ============================================================================
// 缓存内容（内存 + 文件）：
//   ✅ 虚拟机列表（按 vCenter 连接别名分区，持久化到 vm_cache.json）
//   ✅ ESXi 主机名称列表（内存缓存，PowerCLI 操作目标选择）
//   ✅ 资源池名称列表（内存缓存，迁移/克隆目标选择）
//   ✅ 数据存储名称列表（内存缓存，Storage vMotion 目标选择）
//   ✅ 端口组名称列表（内存缓存，网络配置目标选择）
//   ✅ 虚拟机名称列表（内存缓存，快速查找）
//
// 开发规范：
//   内存缓存 → 通过 UpdateResourceCache / GetCachedXxx / ClearResourceCache 访问
//   文件缓存 → 通过 LoadFromCacheFile / SaveToCacheFile 访问
//   新增缓存项 → 在此文件添加字段 + UpdateResourceCache 参数 + Get/Clear 方法
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VirtualizationOpsTool.Core
{
    public static class UnifiedCache
    {
        private const string CacheFileName = "vm_cache.json";
        private const int MemCacheTtlSec = 5 * 60;
        private const int FileCacheMaxVmsPerVc = 2000;

        private static readonly object _fileLock = new object();
        private static readonly ReaderWriterLockSlim _memLock = new ReaderWriterLockSlim();
        
        private static List<string> _cachedHosts = new List<string>();
        private static List<string> _cachedDatastores = new List<string>();
        private static List<string> _cachedPortGroups = new List<string>();
        private static List<string> _cachedVms = new List<string>();
        private static List<string> _cachedResourcePools = new List<string>();

        private static readonly Dictionary<Tuple<string, string>, Tuple<DateTime, Dictionary<string, object>>> _vmDetailCache = 
            new Dictionary<Tuple<string, string>, Tuple<DateTime, Dictionary<string, object>>>();

        public static string CacheFilePath
        {
            get
            {
                string appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string dataDir = Path.Combine(appdata, "VMTool");
                if (!Directory.Exists(dataDir))
                {
                    Directory.CreateDirectory(dataDir);
                }
                return Path.Combine(dataDir, CacheFileName);
            }
        }

        private static string NormalizeKey(string? key)
        {
            return (key ?? "").Trim().ToLower();
        }

        public static void UpdateResourceCache(List<string>? hosts = null, List<string>? datastores = null, 
            List<string>? portGroups = null, List<string>? vms = null, List<string>? resourcePools = null)
        {
            _memLock.EnterWriteLock();
            try
            {
                if (hosts != null) _cachedHosts = Dedup(hosts);
                if (datastores != null) _cachedDatastores = Dedup(datastores);
                if (portGroups != null) _cachedPortGroups = Dedup(portGroups);
                if (vms != null) _cachedVms = Dedup(vms);
                if (resourcePools != null) _cachedResourcePools = Dedup(resourcePools);
            }
            finally
            {
                _memLock.ExitWriteLock();
            }
        }

        public static List<string> GetCachedHosts()
        {
            _memLock.EnterReadLock();
            try { return new List<string>(_cachedHosts); }
            finally { _memLock.ExitReadLock(); }
        }

        public static List<string> GetCachedDatastores()
        {
            _memLock.EnterReadLock();
            try { return new List<string>(_cachedDatastores); }
            finally { _memLock.ExitReadLock(); }
        }

        public static List<string> GetCachedPortGroups()
        {
            _memLock.EnterReadLock();
            try { return new List<string>(_cachedPortGroups); }
            finally { _memLock.ExitReadLock(); }
        }

        public static List<string> GetCachedVms()
        {
            _memLock.EnterReadLock();
            try { return new List<string>(_cachedVms); }
            finally { _memLock.ExitReadLock(); }
        }

        public static List<string> GetCachedResourcePools()
        {
            _memLock.EnterReadLock();
            try { return new List<string>(_cachedResourcePools); }
            finally { _memLock.ExitReadLock(); }
        }

        public static void ClearResourceCache()
        {
            _memLock.EnterWriteLock();
            try
            {
                _cachedHosts.Clear();
                _cachedDatastores.Clear();
                _cachedPortGroups.Clear();
                _cachedVms.Clear();
                _cachedResourcePools.Clear();
            }
            finally
            {
                _memLock.ExitWriteLock();
            }
        }

        public static Dictionary<string, object>? LoadVmDetail(string vc, string vmId)
        {
            var key = Tuple.Create(NormalizeKey(vc), NormalizeKey(vmId));
            _memLock.EnterReadLock();
            try
            {
                if (_vmDetailCache.TryGetValue(key, out var item))
                {
                    if ((DateTime.Now - item.Item1).TotalSeconds < MemCacheTtlSec)
                    {
                        return new Dictionary<string, object>(item.Item2);
                    }
                }
            }
            finally
            {
                _memLock.ExitReadLock();
            }
            return null;
        }

        public static void SaveVmDetail(string vc, string vmId, Dictionary<string, object> data)
        {
            var key = Tuple.Create(NormalizeKey(vc), NormalizeKey(vmId));
            _memLock.EnterWriteLock();
            try
            {
                _vmDetailCache[key] = Tuple.Create(DateTime.Now, new Dictionary<string, object>(data));
            }
            finally
            {
                _memLock.ExitWriteLock();
            }
        }

        public static void InvalidateVmDetail(string vc, string? vmId = null)
        {
            _memLock.EnterWriteLock();
            try
            {
                string vcKey = NormalizeKey(vc);
                if (vmId == null)
                {
                    var keysToRemove = _vmDetailCache.Keys.Where(k => k.Item1 == vcKey).ToList();
                    foreach (var k in keysToRemove)
                    {
                        _vmDetailCache.Remove(k);
                    }
                }
                else
                {
                    var key = Tuple.Create(vcKey, NormalizeKey(vmId));
                    _vmDetailCache.Remove(key);
                }
            }
            finally
            {
                _memLock.ExitWriteLock();
            }
        }

        public static void ClearVmDetailCache()
        {
            _memLock.EnterWriteLock();
            try { _vmDetailCache.Clear(); }
            finally { _memLock.ExitWriteLock(); }
        }

        public static Dictionary<string, object>? CacheLoadResources(string? vcHost = null)
        {
            lock (_fileLock)
            {
                try
                {
                    if (!File.Exists(CacheFilePath))
                        return null;

                    string json = File.ReadAllText(CacheFilePath);
                    var cacheData = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                    if (cacheData == null) return null;

                    if (!cacheData.TryGetValue("resources", out var resourcesObj))
                        return null;

                    Dictionary<string, object>? resources = null;
                    if (resourcesObj is Dictionary<string, object> dict)
                        resources = dict;
                    else if (resourcesObj is JObject jObj)
                        resources = jObj.ToObject<Dictionary<string, object>>();

                    if (resources == null)
                        return null;

                    if (!string.IsNullOrEmpty(vcHost))
                    {
                        string vcKey = NormalizeKey(vcHost);
                        if (resources.TryGetValue(vcKey, out var segmentObj))
                        {
                            if (segmentObj is Dictionary<string, object> segment)
                                return segment;
                            else if (segmentObj is JObject segJObj)
                                return segJObj.ToObject<Dictionary<string, object>>();
                        }
                        return null;
                    }

                    var lastEntry = resources.Values.LastOrDefault();
                    if (lastEntry is Dictionary<string, object> lastSegment)
                        return lastSegment;
                    else if (lastEntry is JObject lastJObj)
                        return lastJObj.ToObject<Dictionary<string, object>>();
                }
                catch { }
                return null;
            }
        }

        public static bool CacheSaveResources(string vcHost, Dictionary<string, object> data)
        {
            if (string.IsNullOrEmpty(vcHost)) return false;

            lock (_fileLock)
            {
                try
                {
                    var cacheData = new Dictionary<string, object>();
                    if (File.Exists(CacheFilePath))
                    {
                        string json = File.ReadAllText(CacheFilePath);
                        var existing = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                        if (existing != null)
                        {
                            cacheData = existing;
                        }
                    }

                    if (!cacheData.TryGetValue("resources", out var resourcesObj) || !(resourcesObj is Dictionary<string, object> resources))
                    {
                        resources = new Dictionary<string, object>();
                        cacheData["resources"] = resources;
                    }

                    string vcKey = NormalizeKey(vcHost);
                    data["vc_host"] = vcHost;
                    data["updated_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    resources[vcKey] = data;

                    string jsonOut = JsonConvert.SerializeObject(cacheData, Formatting.Indented);
                    File.WriteAllText(CacheFilePath, jsonOut);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        private static List<string> Dedup(IEnumerable<string> items)
        {
            var seen = new HashSet<string>();
            var result = new List<string>();
            foreach (var item in items)
            {
                string s = item.Trim();
                if (!string.IsNullOrEmpty(s) && seen.Add(s))
                {
                    result.Add(s);
                }
            }
            return result;
        }
    }
}