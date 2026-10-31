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
            string dir = Path.Combine(appData, "VMTool");
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
                            var cfg = Newtonsoft.Json.JsonConvert.DeserializeObject<RulePasswordConfig
œÛÛŠNÃBˆYˆ
Ù™ÈOH[	‰ˆ\Ýš[™Ë’\Ó[Ü‘[\JÙ™Ë”™Yš^
JH™]\›ˆÙ™ÎÃBˆCBˆCBˆCBˆCBˆØ]ÚÈCBˆ™]\›ˆ[ÃBˆCBƒBˆX›XÈÝ]XÈ›ÚYØ]™T[PÛÛ™šYÊ[T\ÜÝÛÜ™ÛÛ™šYÈÙ™ÊCBˆÃBˆYˆ
Ù™ÈOH[
H™]\›ŽÃBˆÝš[™ÈœÛÛˆH™]ÝÛœÛÙ’œÛÛ‹’œÛÛÛÛ™\”Ù\šX[^™SØš™XÝ
Ù™ÊNÃBˆÝš[™È[˜Üž\YH[˜Üž\
œÛÛŠNÃBˆÝš[™È]HÙ][PÛÛ™šYÔ]

NÃBˆš[K•Üš]P[^
][˜Üž\Y
NÃBˆCBƒBˆX›XÈÝ]XÈÝš[™È[˜Üž\
Ýš[™ÈZ[•^
CBˆÃBˆYˆ
Ýš[™Ë’\Ó[Ü‘[\JZ[•^
JH™]\›ˆZ[•^ÃBˆž]V×Hž]\ÈH[˜ÛÙ[™Ë•UŽ‘Ù]ž]\ÊZ[•^
NÃBˆž]V×H[˜Üž\YH›ÝXÝY]K”›ÝXÝ
ž]\Ë[›ÜK]T›ÝXÝ[Û”ØÛÜKÝ\œ™[\Ù\ŠNÃBˆ™]\›ˆ™Yš^
ÈÛÛ™\•Ð˜\ÙMÝš[™Ê[˜Üž\Y
NÃBˆCBƒBˆX›XÈÝ]XÈÝš[™ÈXÜž\
Ýš[™ÈÝÜ™Y˜[YJCBˆÃBˆYˆ
Ýš[™Ë’\Ó[Ü‘[\JÝÜ™Y˜[YJJH™]\›ˆÝÜ™Y˜[YNÃBƒBˆYˆ
ÝÜ™Y˜[YK”Ý\ÕÚ]
™Yš^
JCBˆÃBˆžCBˆÃBˆž]V×H[˜Üž\YHÛÛ™\‘œ›ÛP˜\ÙMÝš[™ÊÝÜ™Y˜[YK”ÝXœÝš[™Ê™Yš^“[™Ý
JNÃBˆž]V×HZ[ž]\ÈH›ÝXÝY]K•[œ›ÝXÝ
[˜Üž\Y[›ÜK]T›ÝXÝ[Û”ØÛÜKÝ\œ™[\Ù\ŠNÃBˆ™]\›ˆ[˜ÛÙ[™Ë•UŽ‘Ù]Ýš[™ÊZ[ž]\ÊNÃBˆCBˆØ]ÚBˆÃBˆ™]\›ˆ[ÃBˆCBˆCBƒBˆžCBˆÃBˆž]V×HZ[ž]\ÈHÛÛ™\‘œ›ÛP˜\ÙMÝš[™ÊÝÜ™Y˜[YJNÃBˆ™]\›ˆ[˜ÛÙ[™Ë•UŽ‘Ù]Ýš[™ÊZ[ž]\ÊNÃBˆCBˆØ]ÚBˆÃBˆ™]\›ˆÝÜ™Y˜[YNÃBˆCBˆCBˆCBŸ