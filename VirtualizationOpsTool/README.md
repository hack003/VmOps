# 虚拟化运维管理工具 - .NET 版本

## 项目概述

本项目是原 Python + tkinter 版本的虚拟化运维管理工具的完整 .NET 转换版本。

### 原项目信息
- **语言**: Python 3.x
- **GUI框架**: tkinter
- **核心依赖**: pyVmomi, PowerCLI (通过 PowerShell)
- **功能**: VMware vSphere 环境管理工具

### 转换后项目信息
- **语言**: C# (.NET 8.0)
- **GUI框架**: Windows Forms
- **核心依赖**: VMware PowerCLI (通过 PowerShell), SSH.NET, Newtonsoft.Json
- **功能**: 完全保持与原项目一致

## 项目结构

```
VirtualizationOpsTool/
├── VirtualizationOpsTool.sln          # Visual Studio 解决方案文件
├── VirtualizationOpsTool/
│   ├── VirtualizationOpsTool.csproj   # 项目配置文件
│   ├── Program.cs                     # 程序入口
│   ├── MainForm.cs                    # 主窗体
│   ├── Core/                          # 核心模块
│   │   ├── SessionManager.cs          # PowerShell 会话管理
│   │   ├── UnifiedCache.cs            # 统一缓存系统
│   │   └── QueryBuilder.cs            # 查询语句构建器
│   ├── UI/                            # 用户界面模块
│   │   ├── HomeTab.cs                 # 主页 Tab
│   │   ├── ResourceQueryTab.cs        # 资源查询 Tab
│   │   ├── CsvEditorTab.cs            # CSV 编辑 Tab
│   │   └── OVFToolTab.cs              # OVF 工具 Tab
│   ├── Utils/                         # 工具类模块
│   │   ├── DpiUtils.cs                # DPI 缩放工具
│   │   ├── LoggerUtils.cs             # 日志工具
│   │   ├── TableConvertUtils.cs       # 表格转换工具
│   │   ├── CoreUtils.cs               # 核心工具
│   │   ├── AppVersion.cs              # 版本信息
│   │   └── Update.cs                  # 更新检查
│   ├── VmOps/                         # 虚拟机操作模块
│   │   ├── VmOpsPower.cs              # 电源操作
│   │   ├── VmOpsSnapshot.cs           # 快照管理
│   │   └── VmOpsResources.cs          # 资源调整
│   └── Resources/                     # 资源文件
│       ├── vmware.ico                 # 图标文件
│       └── help.html                  # 帮助文档
└── _构建.NET版本.bat                   # 构建脚本
```

## 功能对照表

| 原 Python 模块 | .NET 对应模块 | 功能说明 |
|---------------|--------------|----------|
| main.py | Program.cs + MainForm.cs | 程序入口和主窗体 |
| core/core_session_manager.py | Core/SessionManager.cs | PowerShell 会话管理 |
| core/core_unified_cache.py | Core/UnifiedCache.cs | 统一缓存系统 |
| core/core_query_builder.py | Core/QueryBuilder.cs | 查询语句构建 |
| ui/home.py | UI/HomeTab.cs | 主页连接管理 |
| ui/resource_tab.py | UI/ResourceQueryTab.cs | 资源查询展示 |
| ui/csv_editor_tab.py | UI/CsvEditorTab.cs | CSV 文件编辑 |
| ui/ovftool_tab.py | UI/OVFToolTab.cs | OVF 部署工具 |
| utils/dpi_utils.py | Utils/DpiUtils.cs | DPI 缩放适配 |
| utils/logger_utils.py | Utils/LoggerUtils.cs | 日志输出管理 |
| utils/table_convert_utils.py | Utils/TableConvertUtils.cs | 表格数据转换 |
| utils/core_utils.py | Utils/CoreUtils.cs | 核心工具函数 |
| vm_ops/vm_ops_power.py | VmOps/VmOpsPower.cs | 虚拟机电源操作 |
| vm_ops/vm_ops_snapshot.py | VmOps/VmOpsSnapshot.cs | 虚拟机快照管理 |
| vm_ops/vm_ops_resources.py | VmOps/VmOpsResources.cs | 虚拟机资源调整 |

## 环境要求

### 必需软件
1. **.NET 8.0 SDK** 或更高版本
   - 下载地址: https://dotnet.microsoft.com/download/dotnet/8.0
   - 验证安装: `dotnet --version`

2. **PowerShell 7.x** (推荐) 或 Windows PowerShell 5.1
   - 用于执行 PowerCLI 命令
   - 下载地址: https://github.com/PowerShell/PowerShell

3. **VMware PowerCLI** 模块
   - 安装命令: `Install-Module -Name VMware.PowerCLI -Scope CurrentUser`

### 可选软件
- **Visual Studio 2022** (推荐) 或 VS Code + C# 扩展
- **VMware vSphere** 环境 (用于实际使用)

## 构建说明

### 方法一: 使用批处理脚本 (推荐)

```bash
# 双击运行
_构建.NET版本.bat
```

### 方法二: 使用命令行

```bash
# 进入项目目录
cd VirtualizationOpsTool

# 恢复 NuGet 包
dotnet restore

# 构建 Release 版本
dotnet build --configuration Release

# 运行程序
dotnet run
```

### 方法三: 使用 Visual Studio

1. 双击 `VirtualizationOpsTool.sln` 打开解决方案
2. 右键解决方案 -> "还原 NuGet 包"
3. 按 F5 或点击"启动"按钮运行

## 发布说明

### 独立发布 (无需安装 .NET)

```bash
# 发布为单文件可执行程序
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# 输出目录: VirtualizationOpsTool/bin/Release/net8.0-windows/win-x64/publish/
```

### 依赖框架发布 (需要用户安装 .NET)

```bash
dotnet publish -c Release -r win-x64 --self-contained false
```

## 主要技术特点

### 1. DPI 感知支持
- 自动检测系统 DPI 缩放比例
- 所有控件和字体自动适配高分屏
- 支持 Per-Monitor V2 DPI 感知模式

### 2. 异步编程模型
- 全面使用 async/await 模式
- 避免 UI 线程阻塞
- 支持操作取消 (CancellationToken)

### 3. 线程安全
- UI 操作自动调度到主线程
- 使用 Invoke/BeginInvoke 确保线程安全
- 缓存系统使用 ReaderWriterLockSlim

### 4. PowerShell 集成
- 通过 SessionManager 管理 PowerShell 进程
- 支持同步和异步脚本执行
- 自动处理输出和错误

### 5. 缓存系统
- 双层缓存架构 (内存 + 文件)
- 自动过期机制 (TTL)
- 按 vCenter 分段存储

## 与原 Python 版本的差异

### 技术栈差异
| 特性 | Python 版本 | .NET 版本 |
|------|------------|-----------|
| GUI 框架 | tkinter | Windows Forms |
| 并发模型 | threading | async/await + Task |
| JSON 处理 | json (内置) | Newtonsoft.Json |
| SSH 支持 | paramiko | SSH.NET |
| 打包方式 | PyInstaller | dotnet publish |

### 性能优势
- 启动速度更快 (JIT 编译)
- 内存占用更低 (更好的 GC)
- UI 响应更流畅 (原生 Windows 控件)
- 执行效率更高 (编译型语言)

### 维护优势
- 强类型检查 (编译时发现错误)
- 更好的 IDE 支持 (智能提示、重构)
- 更完善的调试工具
- 更容易的代码审查

## 常见问题

### Q1: 程序无法连接 vCenter
**A**: 请确保:
1. 已安装 VMware PowerCLI 模块
2. PowerShell 可以正常执行 PowerCLI 命令
3. 网络连接正常，端口 443 可访问

### Q2: 编译时提示找不到 .NET SDK
**A**: 请先安装 .NET 8.0 SDK:
```bash
# 验证安装
dotnet --version
```

### Q3: 高分屏下界面模糊
**A**: 程序已内置 DPI 感知支持，如果仍有问题请检查:
1. Windows 显示设置中的缩放比例
2. 程序兼容性设置中是否勾选了"禁用显示缩放"

### Q4: 如何打包成单个 exe 文件
**A**: 使用以下命令:
```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## 开发计划

### 已完成
- [x] 核心架构转换
- [x] UI 框架转换 (tkinter -> WinForms)
- [x] 会话管理模块
- [x] 缓存系统
- [x] 资源查询功能
- [x] CSV 编辑功能
- [x] 虚拟机电源操作
- [x] 快照管理
- [x] 资源调整功能

### 待完成
- [ ] SSH 命令执行窗口
- [ ] 批量部署功能
- [ ] 虚拟机迁移功能
- [ ] 光驱管理功能
- [ ] 配置优化功能
- [ ] 单元测试
- [ ] 安装包制作

## 许可证

本项目仅供学习和技术交流使用。

## 联系方式

如有问题或建议，请提交 Issue。

---

**版本**: 1.6.1.6  
**转换日期**: 2026-09-05  
**目标框架**: .NET 8.0 Windows