// ============================================================================
// AppVersion.cs - åº”ç”¨ç‰ˆæœ¬åûï"9.ã¹ê"ùn£úfáº+¾X[¾ò3š&O–2š^Û¢«–*£¢Ş¦j<5M	Õ¥±ƒ–æ•°ï¼‰
// ============================================================================
// å”¯ä¸€ç‰ˆæœ¬æ¥æºï¼šcsproj çš„ <Version>ï¼Œæˆ–æ‰“åŒ…æ—¶ dotnet publish -p:Version=xxx
// ç¼–è¤y¥íˆÑÈ:!ê¹bª9a¦yaiH\ÜÙ[X›U™\œÚ[Û]šX]{ï#:-ŠÎi{nX7–Â¢»å…‚‹ËÈ:(ªÈ\]K˜ÜÈ:-Îzˆ¾x˜iÊÎjS–¹ã€MainForm æ ‡é¢˜æ ã€å¸®åŠ©æ–‡æ¡£ footer ç­‰å¼•ç”¨
// ============================================================================
using System.Reflection;

namespace VirtualizationOpsTool.Utils
{
    public static class AppVersion
    {
        public static readonly string VERSION_STR =
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
    }
}