// ============================================================================
// AppVersion.cs - 应用版本号（从程序集读取，打包时自动跟随 MSBuild 参数）
// ============================================================================
// 唯一版本来源：csproj 的 <Version>，或打包时 dotnet publish -p:Version=xxx
// 编译时 SDK 自动写入 AssemblyVersionAttribute，运行时反射读取
// 被 Update.cs 远程版本比对、MainForm 标题栏、帮助文档 footer 等引用
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