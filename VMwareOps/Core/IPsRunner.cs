// ============================================================================
// IPsRunner.cs - PowerShell 执行器接口定义
// ============================================================================
// 职责：定义 PowerShell 执行通道的统一契约
//
// 核心接口 IPsRunner：
//   ✅ IsConnected / CurrentVc — 连接状态 + 当前 vCenter 别名
//   ✅ ExecuteScriptAsync() — 执行脚本并返回文本输出（详情查询/操作脚本）
//   ✅ ExecuteQueryAsync() — 执行查询脚本并返回 Dictionary 列表（表格数据）
//   ✅ DisconnectAsync() — 断开通道连接
//
// 实现类：
//   SessionManager（常驻通用通道）/ TempDeploymentRunner（临时部署通道）
// ============================================================================
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace VmOps.Core
{
    public interface IPsRunner : IDisposable
    {
        bool IsConnected { get; }
        string? CurrentVc { get; }

        Task<string> ExecuteScriptAsync(string script, Action<string?, string?>? logCallback = null, int timeoutSec = 60);
        Task<List<Dictionary<string, object>>> ExecuteQueryAsync(string script, Action<string?, string?>? logCallback = null, int timeoutSec = 90);
        Task DisconnectAsync(Action<string?, string?>? logCallback = null);
    }
}