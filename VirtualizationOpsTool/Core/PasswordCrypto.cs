using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace VirtualizationOpsTool.Core
{
    internal static class PasswordCrypto
    {
        private static readonly byte[] Entropy = {
            0x56, 0x4D, 0x54, 0x6F, 0x6F, 0x6C,
            0x5F, 0x43, 0x72, 0x65, 0x64, 0x65, 0x6E, 0x74,
            0x69, 0x61, 0x6C, 0x5F, 0x45, 0x6E, 0x63, 0x72,
            0x79, 0x70, 0x74, 0x5F, 0x56, 0x31
        };

        private const string Prefix = "DPAPI_V1:";
        private const string RuleConfigFileName = "rule_password.cfg";
        private static readonly byte[] RuleEntropy = {
            0x56, 0x4D, 0x54, 0x5F, 0x52, 0x55, 0x4C, 0x45,
            0x5F, 0x43, 0x4F, 0x4E, 0x46, 0x49, 0x47, 0x5F,
            0x56, 0x31
        };

        public class RulePasswordConfig
        {
            public string Prefix = "";
            public string Separator = "#";
            public string Separator2 = "$";
            public int Multiplier = 4;
            public int Offset = 0;
        }

        private static string GetRuleConfigPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "VmOps");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return Path.Combine(dir, RuleConfigFileName);
        }

        public static RulePasswordConfig LoadRuleConfig()
        {
            string path = GetRuleConfigPath();
            try
            {
                if (File.Exists(path))
                {
                    string encrypted = File.ReadAllText(path).Trim();
                    if (!string.IsNullOrEmpty(encrypted))
                    {
                        string json = Decrypt(encrypted);
                        if (!string.IsNullOrEmpty(json))
                        {
                            var cfg = Newtonsoft.Json.JsonConvert.DeserializeObject<RulePasswordConfig>(json);
                            if (cfg != null && !string.IsNullOrEmpty(cfg.Prefix)) return cfg;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        public static void SaveRuleConfig(RulePasswordConfig cfg)
        {
            if (cfg == null) return;
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(cfg);
            string encrypted = Encrypt(json);
            string path = GetRuleConfigPath();
            File.WriteAllText(path, encrypted);
        }

        public static string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return plainText;
            byte[] bytes = Encoding.UTF8.GetBytes(plainText);
            byte[] encrypted = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(encrypted);
        }

        public static string Decrypt(string storedValue)
        {
            if (string.IsNullOrEmpty(storedValue)) return storedValue;

            if (storedValue.StartsWith(Prefix))
            {
                try
                {
                    byte[] encrypted = Convert.FromBase64String(storedValue.Substring(Prefix.Length));
                    byte[] plainBytes = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(plainBytes);
                }
                catch
                {
                    return null;
                }
            }

            try
            {
                byte[] plainBytes = Convert.FromBase64String(storedValue);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch
            {
                return storedValue;
            }
        }
    }
}