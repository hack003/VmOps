
# VmOps - 虚拟化运维管理工具

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.8-blue.svg)](https://dotnet.microsoft.com/)
[![PowerCLI](https://img.shields.io/badge/VMware-PowerCLI-676767.svg)](https://developer.broadcom.com/tools/vmware-powercli/latest/)

**VmOps** 是一款专为企业虚拟化维护人员设计的批量虚拟机部署与自动化配置工具，基于 **C# (.NET Framework 4.8)** + **VMware PowerCLI** + **Get-View VIM API** 构建。

本工具已在实际生产环境中长期稳定运行，适用于需要频繁批量部署虚拟机及日常运维的企业场景。

> ⚠️ **本工具为个人兴趣开发，永久免费，请勿商用或售卖。**

---

## ✨ 核心功能

| 模块 | 说明 |
|------|------|
| 🚀 **批量部署虚拟机** | 通过 CSV 模板批量定义配置（vCPU/内存/存储/网络/网关/掩码），一键完成创建与部署 |
| 📋 **资源查询** | 实时查询虚拟机、主机、存储、网络列表；支持搜索过滤、导出 CSV、双击详情 |
| ⚙️ **VM 日常运维** | 分组右键菜单：远程连接、电源管理、编辑配置（智能升降）、磁盘管理、迁移克隆、快照管理 |
| 🔥 **批量操作** | 多选 VM 后批量执行电源、快照、删除、vMotion 迁移等 |
| 🖥 **VMRC 远程控制台** | 一键打开 VMware Remote Console 图形控制台（自动获取免密票据） |
| 📝 **在线编辑 CSV** | 内置表格编辑器，导入 / 编辑 / 保存部署模板，无需外部工具 |
| 📦 **OVF Tool 集成** | 支持导出 / 导入 / 转换 OVF、OVA、VMX 格式 |
| 🔐 **凭据记忆** | 自动保存 vCenter / ESXi 连接配置，支持连接别名，减少重复输入 |

---

## 🛠 技术栈

- **语言**：C# 8.0（可空引用类型）
- **框架**：.NET Framework 4.8 + WinForms（DPI 自适应）
- **虚拟化 API**：VMware PowerCLI + `Get-View` VIM API
- **PowerShell 架构**：双通道 Runspace（常驻通用通道 + 临时部署通道）
- **依赖合并**：Costura.Fody（单 exe 分发）
- **第三方库**：Newtonsoft.Json、SSH.NET

---

## 📦 系统要求

- **操作系统**：Windows 10 / 11 / Server 2016+（x64）
- **.NET Framework**：4.8（Win10/11 已自带）
- **VMware PowerCLI**：任意版本（支持在线安装 / 脱机安装）
- **支持的 vSphere**：vCenter Server 6.7+ / ESXi 6.5+

---

## 🚀 快速开始

### 1. 安装 VMware PowerCLI（必装）

#### 方式一：在线安装（推荐）
```powershell
Set-ExecutionPolicy RemoteSigned -Scope CurrentUser
Set-PSRepository -Name PSGallery -InstallationPolicy Trusted
Install-Module -Name VMware.PowerCLI
```

#### 方式二：脱机安装
从 Broadcom 官方或内部文件服务器下载 `VMware-PowerCLI-*.zip`，解压到 `C:\Program Files\WindowsPowerShell\Modules\`，然后执行：
```powershell
Get-ChildItem "C:\Program Files\WindowsPowerShell\Modules\VMware*" -Recurse -File | Unblock-File
```

### 2. 下载 VmOps 可执行文件

前往 **[Releases](https://gitee.com/xuGH/VmOps-Tool/releases)** 页面下载最新版本压缩包，解压后直接运行 `VirtualizationOpsTool.exe`（无需安装，单文件分发）。

### 3. 连接 vCenter / ESXi

打开 VmOps → 主页 Tab → 填写连接信息 → 开始连接：

| 字段 | vCenter 示例 | ESXi 示例 |
|------|-------------|-----------|
| 连接别名 | 生产VC | ESXi-Node01 |
| 连接地址 | 192.168.200.88 | 192.168.1.200 |
| 用户域 | vsphere.local | _（留空）_ |
| 用户名 | administrator | root |

> 💡 **自动识别**：用户名为 `root` 时自动 ESXi 直连模式；其他用户名自动 vCenter 模式。

### 4. 开始使用

- **资源查询**：切换到虚拟机 / 主机 / 存储 / 网络 Tab，自动查询列表
- **批量部署**：编辑 CSV 模板 → 执行部署
- **VM 运维**：右键虚拟机执行电源、配置、快照、迁移等操作

---

## 📖 功能模块详解

### 资源查询 Tab
- 支持搜索过滤、复制选中、导出 CSV、双击查看详情
- 虚拟机 Tab 含子类型切换（虚拟机 / 模板）
- 右键菜单分组下拉，按风险级别聚合

### VM 右键菜单结构
```
远程连接 (🟢)      → VMRC / RDP / SSH
电源管理 (🟢)      → 开机 / 关机 / 强制断电 / Reset
编辑配置 (🟡)      → vCPU智能升降 / 内存智能升降 / 端口组 / GuestOS网络 / 密码
磁盘管理 (🟡)      → 列表 / 新增 / 扩容 / 删除
迁移克隆 (🟡)      → vMotion主机 / 资源池 / Storage vMotion / 克隆
快照管理 (🟡)      → 查看 / 创建 / 回滚最新 / 删除所有
高级操作 (🟡)      → 断开光驱 / 优化配置 / 转模板 / 删除虚拟机
```

### 批量操作
- 多选 VM 后右键出现「⚡ 批量操作」子菜单
- 支持：批量电源、批量快照、批量删除（自动跳过开机 VM）、批量 vMotion

---

## 📂 程序数据

所有数据文件统一存储在 `%APPDATA%\VMTool\` 目录：

| 文件 | 说明 |
|------|------|
| `credentials.json` | 连接凭据（地址、域、用户名、Base64 密码、别名） |
| `deploy_vm_list.csv` | CSV 部署模板（UTF-8 BOM 编码） |
| `vm_cache.json` | 资源缓存（按服务器 IP 分段） |

---

## 🏗️ 项目结构

```
VmOps-Tool/
├── VirtualizationOpsTool/
│   ├── MainForm.cs              # 主窗口（状态栏 + Tab 容器）
│   ├── Program.cs               # 程序入口
│   ├── Core/
│   │   ├── QueryBuilder.cs      # VIM API 查询构建器
│   │   ├── SessionManager.cs    # vCenter/ESXi 会话管理
│   │   ├── GuestOpsHelper.cs    # GuestOS 操作辅助
│   │   └── RunspaceManager.cs   # PowerShell Runspace 双通道
│   ├── UI/
│   │   ├── HomeTab.cs           # 主页（连接配置 + 快捷导航）
│   │   ├── ResourceQueryTab.cs  # 资源查询 Tab（VM/Host/Datastore/Network）
│   │   ├── CsvEditorTab.cs      # 在线 CSV 编辑器 Tab
│   │   └── OVFToolTab.cs        # OVF Tool 命令生成器 Tab
│   ├── Utils/
│   │   ├── AppVersion.cs        # 版本号（从 csproj 自动读取）
│   │   ├── Update.cs            # 远程版本检查
│   │   ├── CoreUtils.cs         # 通用工具方法
│   │   └── DpiUtils.cs          # DPI 自适应工具
│   ├── scripts/                 # 内嵌 PowerShell / Shell 脚本
│   ├── Resources/               # 图标 + help.html
│   └── VirtualizationOpsTool.csproj
└── Releases/                    # 打包输出
```

---

## 🔧 关于 VIM API 的设计

本项目 vCenter 操作**统一使用 Get-View + VIM API**：

- **查询**：`Get-View -ViewType ... -Property ...`
- **写操作**：调用 `*_Task()` 方法，参数传 `.MoRef`，Task 轮询 `Get-View -Id $task.MoRef` 看 `Info.State`
- **禁用**：`Get-VM` / `New-VM` / `Move-VM` / `Set-VM` / `Start-VM` / `Stop-VM` / `New-Snapshot` 等高级 cmdlet，保证代码风格统一

---

## ⚠️ 免责声明

本工具由个人兴趣开发并免费提供，**仅供内部学习和运维使用**。作者不对任何生产环境使用后果负责，使用前请充分测试。**禁止商用、售卖或作为商业产品的一部分分发。**

---

## 📄 许可证

本项目基于 [MIT License](https://opensource.org/licenses/MIT) 开源。

```
Copyright (c) 2024 VmOps Team

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## 🙏 致谢

- **VMware PowerCLI** —— 本工具的核心依赖
- **Costura.Fody** —— 单 exe 分发方案
- **所有内部测试用户** —— 生产环境反馈与改进建议




