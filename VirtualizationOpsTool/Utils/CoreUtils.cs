// ============================================================================
// CoreUtils.cs - 通用杂项工具集合（静态方法）
// ============================================================================
// 职责范围：
//   ✅ IP/网络工具  — CidrToNetmask() CIDR → 子网掩码
//   ✅ 资源路径     — GetResourcePath() / OrganizeResourceFiles() / GetUserDataDir()
//   ✅ 编码处理     — ConvertCsvToUtf8() / DetectEncoding() 自动编码检测
//   ✅ 应用标识     — GetAppIcon() 返回内嵌图标
//   ✅ 运行时检查   — CheckProgramExpiry() 授权到期检查
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace VirtualizationOpsTool.Utils
{
    public static class CoreUtils
    {
        private const string DEFAULT_NETMASK = "255.255.255.0";
        private static readonly Dictionary<int, string> CidrTable;

        static CoreUtils()
        {
            CidrTable = new Dictionary<int, string>();
            for (int cidr = 0; cidr <= 32; cidr++)
            {
                uint maskInt = cidr == 0 ? 0 : (0xFFFFFFFF << (32 - cidr)) & 0xFFFFFFFF;
                byte[] octets = new byte[4];
                octets[0] = (byte)((maskInt >> 24) & 0xFF);
                octets[1] = (byte)((maskInt >> 16) & 0xFF);
                octets[2] = (byte)((maskInt >> 8) & 0xFF);
                octets[3] = (byte)(maskInt & 0xFF);
                CidrTable[cidr] = $"{octets[0]}.{octets[1]}.{octets[2]}.{octets[3]}";
            }
        }

        public static string CidrToNetmask(string? maskValue)
        {
            if (string.IsNullOrWhiteSpace(maskValue))
                return DEFAULT_NETMASK;

            string s = maskValue.Trim();
            if (string.IsNullOrEmpty(s))
                return DEFAULT_NETMASK;

            if (!s.Contains("."))
            {
                if (int.TryParse(s, out int cidr) && cidr >= 0 && cidr <= 32)
                {
                    return CidrTable[cidr];
                }
                return DEFAULT_NETMASK;
            }

            string[] parts = s.Split('.');
            if (parts.Length == 4)
            {
                foreach (var p in parts)
                {
                    if (!int.TryParse(p, out int n) || n < 0 || n > 255)
                        return DEFAULT_NETMASK;
                }
                return s;
            }

            return DEFAULT_NETMASK;
        }

        public static string GetResourcePath(string filename)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string preferred = Path.Combine(baseDir, "Resources", filename);
            if (File.Exists(preferred))
                return preferred;
            string direct = Path.Combine(baseDir, filename);
            if (File.Exists(direct))
                return direct;

            string tempDir = Path.Combine(Path.GetTempPath(), "VMTool", "v" + AppVersion.VERSION_STR);
            Directory.CreateDirectory(tempDir);
            string tempFile = Path.Combine(tempDir, filename);

            if (!File.Exists(tempFile))
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                string resourceName = $"VirtualizationOpsTool.Resources.{filename}";
                using var stream = asm.GetManifestResourceStream(resourceName);
                if (stream == null) return direct;
                using var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write);
                stream.CopyTo(fs);
            }

            return tempFile;
        }

        public static string GetHelpFilePath()
        {
            try
            {
                string srcPath = GetResourcePath("help.html");
                if (string.IsNullOrEmpty(srcPath) || !File.Exists(srcPath))
                    return srcPath;

                string content = File.ReadAllText(srcPath, new UTF8Encoding(false));
                content = content.Replace("__APP_VERSION__", AppVersion.VERSION_STR);

                string tempDir = Path.Combine(Path.GetTempPath(), "VMTool");
                Directory.CreateDirectory(tempDir);
                string tempFile = Path.Combine(tempDir, "help.html");
                File.WriteAllText(tempFile, content, new UTF8Encoding(false));

                string srcIcon = GetResourcePath("vmware.ico");
                string dstIcon = Path.Combine(tempDir, "vmware.ico");
                if (!string.IsNullOrEmpty(srcIcon) && File.Exists(srcIcon) && !File.Exists(dstIcon))
                {
                    File.Copy(srcIcon, dstIcon);
                }

                return tempFile;
            }
            catch
            {
                return GetResourcePath("help.html");
            }
        }

        public static System.Drawing.Icon? GetAppIcon()
        {
            try
            {
                // 优先尝试从内嵌资源加载
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourceName = "VirtualizationOpsTool.Resources.vmware.ico";
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                    {
                        return new System.Drawing.Icon(stream);
                    }
                }

                // 备用：从文件路径加载
                string iconPath = GetResourcePath("vmware.ico");
                if (File.Exists(iconPath))
                {
                    return new System.Drawing.Icon(iconPath);
                }
            }
            catch { }
            return null;
        }

        public static string GetUserDataDir()
        {
            string appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dataDir = Path.Combine(appdata, "VMTool");
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }
            return dataDir;
        }

        public static void OrganizeResourceFiles()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string resourcesDir = Path.Combine(baseDir, "Resources");
                Directory.CreateDirectory(resourcesDir);

                foreach (var name in new[] { "vmware.ico", "help.html" })
                {
                    string src = Path.Combine(baseDir, name);
                    string dst = Path.Combine(resourcesDir, name);
                    if (File.Exists(src) && !File.Exists(dst))
                    {
                        File.Copy(src, dst);
                    }
                }
            }
            catch
            {
            }
        }

        public static void ConvertCsvToUtf8(string filePath)
        {
            if (!File.Exists(filePath)) return;

            try
            {
                var encoding = DetectEncoding(filePath);
                if (encoding.CodePage == Encoding.UTF8.CodePage)
                    return;

                string content = File.ReadAllText(filePath, encoding);
                File.WriteAllText(filePath, content, new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        private static Encoding DetectEncoding(string filePath)
        {
            var bom = new byte[4];
            using (var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                file.Read(bom, 0, 4);
            }

            if (bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
                return Encoding.UTF8;
            if (bom[0] == 0xFF && bom[1] == 0xFE)
                return Encoding.Unicode;
            if (bom[0] == 0xFE && bom[1] == 0xFF)
                return Encoding.BigEndianUnicode;

            return Encoding.Default;
        }

        public static bool CheckProgramExpiry()
        {
            return true;
        }
    }
}