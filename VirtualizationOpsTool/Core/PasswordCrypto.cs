using System;
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