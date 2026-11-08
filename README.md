
# VmOps - 虚拟化运维管理工具

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.8-blue.svg)](https://dotnet.microsoft.com/)
[![PowerCLI](https://img.shields.io/badge/VMware-PowerCLI-676767.svg)](https://developer.broadcom.com/tools/vmware-powercli/latest/)
[![OWIN](https://img.shields.io/badge/Web%20API-OWIN-green.svg)](https://github.com/owin/owin.github.io)

**VmOps** 是一款专为企业虚拟化维护人员设计的批量虚拟机部署与自动化配置工具，基于 **C# (.NET Framework 4.8)** + **VMware PowerCLI** + **Get-View VIM API** 构建。

本工具已在实际生产环境中长期稳定运行，适用于需要频繁批量部署虚拟机及日常运维的企业场景。

国内项目地址：https://gitee.com/hack003/VmOps

github项目地址：https://github.com/hack003/VmOps

---

## ✨ 核心功能

| 模块 | 说明 |
|------|------|
| 🚀 **批量部署虚拟机** | 通过 CSV 模板批量定义配置（vCPU/内存/存储/网络/网关/掩码），一键完成创建与部署 |
| 📋 **资源查询** | 实时查询虚拟机、主机、存储、网络列表；支持搜索过滤、导出 CSV、双击详情 |
| ⚙️ **VM 日常运维** | 分组右键菜单：远程连接、电源管理、编辑配置（智能升降）、磁盘管理、迁移克隆、快照管理 |
| 🔥 **批量操作** | 多选 VM 后批量执行电源、快照、删除、vMotion 迁移等 |
| 🖥 **VMRC 远程控制台** | 一键打开 VMware Remote Console 图形控制台 |
| 📝 **在线编辑 CSV** | 内置表格编辑器，导入 / 编辑 / 保存部署模板，无需外部工具 |
| 📦 **OVF Tool 集成** | 支持导出 / 导入 / 转换 OVF、OVA、VMX 格式 |
| 🔐 **凭据记忆** | 自动保存 vCenter / ESXi 连接配置，支持连接别名，减少重复输入 |
| 🌐 **REST Web API** | 内置 OWIN 自托管 Web 服务，提供完整 REST API，支持前端扩展和二次开发 |
| 🔌 **扩展系统** | 通过 Gitee 远程仓库下载安装前端扩展，按需加载，不占用核心程序体积 |

---

## 🛠 技术栈

- **语言**：C# 8.0（可空引用类型）
- **框架**：.NET Framework 4.8 + WinForms（DPI 自适应）
- **虚拟化 API**：VMware PowerCLI + `Get-View` VIM API
- **PowerShell 架构**：双通道 Runspace（常驻通用通道 + 临时部署通道）
- **Web 服务**：OWIN + ASP.NET Web API 2（自托管，无需 IIS）
- **依赖合并**：Costura.Fody（单 exe 分发）
- **第三方库**：Newtonsoft.Json、SSH.NET、Microsoft.Owin

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

前往 **[Gitee Releases](https://gitee.com/hack003/VmOps/releases)** 页面下载最新版本 `VmOps.NET.exe`（无需安装，单文件分发）。

前往 **[github Releases](https://github.com/hack003/VmOps/releases)** 页面下载最新版本 `VmOps.NET.exe`（无需安装，单文件分发）。

### 3. 连接 vCenter / ESXi

打开 VmOps → 主页 Tab → 填写连接信息 → 开始连接：

| 字段 | vCenter 示例 | ESXi 示例 |
|------|-------------|-----------|
| 连接别名 | 生产VC | ESXi-Node01 |
| 连接地址 | 192.168.2.88 | 192.168.1.200 |
| 用户域 | vsphere.local | _（留空）_ |
| 用户名 | administrator | root |

> 💡 **自动识别**：用户名为 `root` 时自动 ESXi 直连模式；其他用户名自动 vCenter 模式。

### 4. 开始使用

- **资源查询**：切换到虚拟机 / 主机 / 存储 / 网络 Tab，自动查询列表
- **批量部署**：编辑 CSV 模板 → 执行部署
- **VM 运维**：右键虚拟机执行电源、配置、快照、迁移等操作
- **Web API**：启动后自动监听 `http://localhost:5036`，可直接访问 REST 接口

---

## 🌐 Web API 参考

启动程序后 OWIN Web API 自动运行，默认监听 `http://localhost:5036`。所有接口返回 JSON 格式，已启用 CORS（允许任意来源）。

### 通用说明

| 项目 | 值 |
|------|-----|
| Base URL | `http://localhost:5036` |
| Content-Type | `application/json` |
| 认证 | 无（API 与 WinForms 共享同一 SessionManager，连接状态由程序内控制） |
| 未连接响应 | HTTP 401 `{"error": "not connected"}` |

---

### Session 管理

#### `GET /api/session/status`
查询当前连接状态。

**响应：**
```json
{
  "connected": true,
  "vc": "192.168.2.88",
  "domain": "vsphere.local",
  "taskRunning": false
}
```

---

#### `POST /api/session/connect`
连接 vCenter / ESXi。

**请求体：**
```json
{
  "vc": "192.168.2.88",
  "user": "administrator",
  "password": "your-password",
  "domain": "vsphere.local"
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| vc | string | ✅ | vCenter 或 ESXi 地址 |
| user | string | ✅ | 用户名 |
| password | string | ✅ | 密码 |
| domain | string | ❌ | 用户域，ESXi 留空 |

**成功响应：**
```json
{ "success": true, "vc": "192.168.2.88" }
```

---

#### `POST /api/session/disconnect`
断开当前连接。

**响应：**
```json
{ "success": true }
```

---

### 虚拟机管理

#### `GET /api/vm`
查询所有虚拟机列表。

**查询参数：** 无（自动分页/缓存）

**响应：**
```json
[
  {
    "MoRef": "vm-12345",
    "Name": "Web-Server-01",
    "PowerState": "PoweredOn",
    "NumCpu": 4,
    "MemoryGB": 16,
    "GuestOS": "CentOS 7 (64-bit)",
    "Cluster": "Production-Cluster",
    "Host": "esxi-host-01"
  }
]
```

---

#### `GET /api/vm/templates`
查询所有虚拟机模板。

**响应：**
```json
[
  { "MoRef": "vm-20001", "Name": "CentOS7-Template", "GuestOS": "CentOS 7 (64-bit)", "Host": "esxi-host-01" }
]
```

---

#### `GET /api/vm/{id}/detail`
查询虚拟机详情（CPU、内存、磁盘、网卡完整信息）。

**路径参数：** `id` = 虚拟机 MoRef（如 `vm-12345`）

**响应：**
```json
{
  "MoRef": "vm-12345",
  "Name": "Web-Server-01",
  "NumCpu": 4,
  "MemoryMB": 16384,
  "MemoryGB": 16,
  "OS": "CentOS 7 (64-bit)",
  "PowerState": "PoweredOn",
  "Disks": [
    { "Name": "Hard disk 1", "CapacityGB": 100, "Datastore": "datastore-local" }
  ],
  "NICs": [
    { "Name": "Network adapter 1", "Mac": "00:50:56:12:34:56", "Network": "PortGroup-VLAN100", "Connected": true }
  ]
}
```

---

#### `POST /api/vm/{id}/power`
虚拟机电源操作。

**请求体：**
```json
{ "action": "on" }
```

| action 值 | 说明 |
|-----------|------|
| `on` | 开机 |
| `off` | 关机（Guest OS 优雅关机） |
| `offhard` | 强制断电 |
| `restart` | 重启 |
| `suspend` | 挂起 |
| `reset` | 强制重置 |

**响应：**
```json
{ "success": true }
```

---

#### `GET /api/vm/{id}/snapshots`
查询虚拟机快照列表。

**响应：**
```json
[
  { "Name": "Before-Update", "Description": "系统更新前备份", "Created": "2026-11-01T10:00:00", "SizeGB": 2.5 }
]
```

---

#### `POST /api/vm/{id}/snapshots`
创建快照。

**请求体：**
```json
{ "name": "Before-Update", "description": "系统更新前备份" }
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| name | string | ✅ | 快照名称 |
| description | string | ❌ | 快照描述 |

**响应：**
```json
{ "success": true }
```

---

#### `POST /api/vm/{id}/snapshots/{name}/revert`
回滚到指定快照。

**响应：**
```json
{ "success": true }
```

---

#### `DELETE /api/vm/{id}/snapshots/{name}`
删除指定快照。

**响应：**
```json
{ "success": true }
```

---

#### `PUT /api/vm/{id}/cpu`
调整虚拟机 vCPU 数量。

**请求体：**
```json
{ "count": 8 }
```

**响应：**
```json
{ "success": true }
```

---

#### `PUT /api/vm/{id}/memory`
调整虚拟机内存（MB）。

**请求体：**
```json
{ "memoryMB": 32768 }
```

**响应：**
```json
{ "success": true }
```

---

#### `POST /api/vm/{id}/disk`
新增虚拟磁盘。

**请求体：**
```json
{ "capacityGB": 200, "datastore": "datastore-ssd" }
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| capacityGB | int | ✅ | 磁盘容量（GB） |
| datastore | string | ❌ | 指定存储，留空使用默认 |

**响应：**
```json
{ "success": true }
```

---

### 主机管理

#### `GET /api/host`
查询所有 ESXi 主机列表。

**响应：**
```json
[
  {
    "MoRef": "host-500",
    "Name": "esxi-host-01.example.com",
    "PowerState": "Connected",
    "CpuCores": 16,
    "MemoryGB": 256,
    "Cluster": "Production-Cluster"
  }
]
```

---

#### `GET /api/host/{id}/detail`
查询主机详情。

---

#### `GET /api/host/{id}/vms`
查询指定主机上的所有虚拟机。

---

### 存储管理

#### `GET /api/datastore`
查询所有数据存储列表。

**响应：**
```json
[
  {
    "MoRef": "datastore-700",
    "Name": "datastore-ssd-01",
    "TotalGB": 2048,
    "FreeGB": 512,
    "UsedPercent": 75,
    "Type": "VMFS"
  }
]
```

---

### 集群管理

#### `GET /api/cluster`
查询所有集群列表。

**响应：**
```json
[
  { "MoRef": "clustercompute-100", "Name": "Production-Cluster", "HostCount": 8, "VMPerHostAvg": 15 }
]
```

---

### 仪表盘

#### `GET /api/dashboard/stats`
获取 vCenter 总体统计数据。

**响应：**
```json
{
  "TotalVMs": 120,
  "VMsPoweredOn": 95,
  "VMsPoweredOff": 25,
  "TotalHosts": 8,
  "HostsConnected": 8,
  "TotalClusters": 2,
  "TotalDatastores": 6,
  "TotalStorageGB": 12288,
  "FreeStorageGB": 3072,
  "StorageUsedPercent": 75,
  "TotalCPUCores": 128,
  "TotalMemoryGB": 2048
}
```

---

### 批量操作

#### `POST /api/batch/power`
批量电源操作。

**请求体：**
```json
{
  "ids": ["vm-12345", "vm-12346", "vm-12347"],
  "action": "off"
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| ids | string[] | ✅ | 虚拟机 MoRef 数组 |
| action | string | ✅ | `on` / `off` / `offhard` / `restart` / `suspend` / `reset` |

**响应：**
```json
{
  "total": 3,
  "success": 2,
  "fail": 1,
  "results": [
    { "MoRef": "vm-12345", "Name": "VM-01", "Success": true },
    { "MoRef": "vm-12346", "Name": "VM-02", "Success": true },
    { "MoRef": "vm-12347", "Name": "VM-03", "Success": false, "Error": "VMware.Vim.InvalidPowerState" }
  ]
}
```

---

#### `POST /api/batch/snapshot`
批量快照操作。

**请求体：**
```json
{
  "ids": ["vm-12345", "vm-12346"],
  "action": "create",
  "name": "Pre-Update",
  "description": "批量更新前备份"
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| ids | string[] | ✅ | 虚拟机 MoRef 数组 |
| action | string | ✅ | `create` / `revert-latest` / `remove-all` |
| name | string | 创建时必填 | 快照名称 |
| description | string | ❌ | 快照描述 |

**响应格式同批量电源。**

---

### 扩展管理

#### `GET /api/extensions`
列出所有已安装扩展。

**响应：**
```json
[
  { "id": "webserver", "name": "Web 管理控制台", "version": "1.0.0", "enabled": true, "installedAt": "2026-11-08T15:30:00" }
]
```

---

#### `GET /api/extensions/enabled`
列出已启用扩展。

**响应：**
```json
[
  { "id": "webserver", "name": "Web 管理控制台", "version": "1.0.0" }
]
```

---

#### `POST /api/extensions/{id}/enable`
启用扩展。**响应：** `{ "success": true }`

#### `POST /api/extensions/{id}/disable`
禁用扩展。**响应：** `{ "success": true }`

#### `DELETE /api/extensions/{id}`
卸载扩展。**响应：** `{ "success": true }`

---

#### `POST /api/extensions/install`
安装扩展（从指定 URL 下载 zip）。

**请求体：**
```json
{
  "id": "webserver",
  "packageUrl": "https://gitee.com/hack003/vmops-webserver/releases/download/v1.0.0/vmops-webserver-v1.0.0.zip"
}
```

**响应：**
```json
{ "success": true, "version": "1.0.0", "name": "Web 管理控制台" }
```

---

#### `GET /api/extensions/market?registry=URL`
从远程仓库获取扩展市场列表。

**示例：** `GET /api/extensions/market?registry=https://gitee.com/hack003/vmops-webserver/raw/master/registry.json`

**响应：**
```json
[
  { "id": "webserver", "name": "Web 管理控制台", "version": "1.0.0", "description": "...", "packageUrl": "..." }
]
```

---

## 🔌 扩展开发指南

VmOps.NET 采用**核心程序 + 外部扩展**架构，前端功能全部作为独立扩展按需加载。核心程序体积固定不变，扩展通过 Gitee 远程仓库分发。

### 扩展存储位置

```
%APPDATA%\VmOps\
├── credentials.json       ← 连接凭据
├── extensions.json       ← 扩展注册表（核心程序自动维护）
└── extensions\           ← 扩展根目录
    └── <扩展ID>\
        ├── extension.json    ← 扩展清单（必须）
        ├── index.html        ← 前端入口（可选）
        ├── css\
        └── js\
```

### 扩展包格式（ZIP）

```
my-extension-v1.0.0.zip
├── extension.json         ← 必须，扩展元数据清单
├── index.html             ← 前端入口
├── css/
│   └── styles.css
└── js/
    └── app.js
```

### extension.json 清单格式

```json
{
  "id": "my-extension",
  "name": "我的扩展",
  "version": "1.0.0",
  "description": "扩展功能描述"
}
```

| 字段 | 必填 | 说明 |
|------|------|------|
| id | ✅ | 扩展唯一 ID，决定解压后的目录名 |
| name | ✅ | 扩展显示名称 |
| version | ✅ | 版本号（语义化版本） |
| description | ❌ | 扩展描述 |

### 扩展静态文件路由规则

启用后的扩展通过以下 URL 模式访问：

| 扩展 id | 文件位置 | 访问 URL |
|---------|---------|---------|
| `webserver` | `extensions/webserver/index.html` | `http://localhost:5036/index.html` |
| `backup` | `extensions/backup/index.html` | `http://localhost:5036/extensions/backup/index.html` |

- **主扩展**（如 webserver）：文件直接映射到根路径，无需 `/extensions/` 前缀
- **其他扩展**：通过 `/extensions/{id}/` 前缀访问

### 扩展可调用的 API

前端扩展可直接调用 Web API 的所有接口（CORS 已全开）：

```javascript
// 检查连接状态
fetch('/api/session/status').then(r => r.json()).then(data => console.log(data));

// 列出虚拟机
fetch('/api/vm').then(r => r.json()).then(vms => console.log(vms));

// 执行批量关机
fetch('/api/batch/power', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json' },
  body: JSON.stringify({ ids: ['vm-12345', 'vm-12346'], action: 'off' })
}).then(r => r.json()).then(result => console.log(result));
```

### 发布扩展到 Gitee

1. **创建仓库**（如 `vmops-extensions`）
2. **上传 `registry.json`** 到仓库根目录：
   ```json
   {
     "extensions": [
       {
         "id": "my-extension",
         "name": "我的扩展",
         "version": "1.0.0",
         "description": "扩展描述",
         "packageUrl": "https://gitee.com/hack003/vmops-extensions/releases/download/my-extension-v1.0.0/my-extension-v1.0.0.zip"
       }
     ]
   }
   ```
3. **创建 Release** 并上传 zip 包
4. **在扩展管理器中安装**：右键 → 安装扩展... → 从市场选择

---

## 📂 程序数据

所有数据文件统一存储在 `%APPDATA%\VmOps\` 目录：

| 文件 | 说明 |
|------|------|
| `credentials.json` | 连接凭据（地址、域、用户名、DPAPI 加密密码、别名） |
| `deploy_vm_list.csv` | CSV 部署模板（UTF-8 BOM 编码） |
| `vm_cache.json` | 资源缓存（按服务器 IP 分段） |
| `extensions.json` | 扩展注册表（已安装扩展列表与启用状态） |
| `extensions\` | 扩展根目录（每个扩展一个子目录） |

---

## 🏗️ 项目结构

```
VmOps-Tool/
├── VMwareOps/
│   ├── MainForm.cs              # 主窗口（状态栏 + Tab 容器）
│   ├── Program.cs               # 程序入口（启动 OWIN Web API）
│   ├── Core/
│   │   ├── QueryBuilder.cs      # VIM API 查询构建器
│   │   ├── SessionManager.cs    # vCenter/ESXi 会话管理 + PowerShell Runspace
│   │   ├── ExtensionManager.cs  # 扩展下载/安装/启用管理
│   │   ├── GuestOpsHelper.cs    # GuestOS 操作辅助
│   │   └── RunspaceManager.cs   # PowerShell Runspace 双通道
│   ├── WebApi/
│   │   ├── Startup.cs           # OWIN 配置 + 静态文件服务（扩展目录）
│   │   └── Controllers/
│   │       ├── SessionController.cs   # 连接状态/连接/断开
│   │       ├── VmController.cs        # 虚拟机 CRUD/电源/快照/配置
│   │       ├── HostController.cs      # ESXi 主机查询
│   │       ├── DatastoreController.cs # 数据存储查询
│   │       ├── ClusterController.cs   # 集群查询
│   │       ├── DashboardController.cs # 仪表盘聚合统计
│   │       ├── BatchController.cs     # 批量电源/快照操作
│   │       └── ExtensionController.cs # 扩展安装/启用/卸载/市场
│   ├── UI/
│   │   ├── HomeTab.cs           # 主页（连接配置 + 快捷导航）
│   │   ├── ResourceQueryTab.cs  # 资源查询 Tab（VM/Host/Datastore/Network）
│   │   ├── CsvEditorTab.cs      # 在线 CSV 编辑器 Tab
│   │   ├── OVFToolTab.cs        # OVF Tool 命令生成器 Tab
│   │   └── ExtensionManagerDialog.cs # 扩展管理对话框
│   ├── Utils/
│   │   ├── AppVersion.cs        # 版本号（从 csproj 自动读取）
│   │   ├── Update.cs            # 远程版本检查
│   │   ├── CoreUtils.cs         # 通用工具方法
│   │   └── DpiUtils.cs          # DPI 自适应工具
│   ├── scripts/                 # 内嵌 PowerShell / Shell 脚本
│   ├── Resources/               # 图标 + help.html
│   └── VMwareOps.csproj
└── Releases/                    # 打包输出
```

---

## 🔧 关于 VIM API 的设计

本项目 vCenter 操作**统一使用底层原生 Get-View + VIM API**：

- **查询**：`Get-View -ViewType ... -Property ...`
- **写操作**：调用 `*_Task()` 方法，参数传 `.MoRef`，Task 轮询 `Get-View -Id $task.MoRef` 看 `Info.State`
- **禁用**：`Get-VM` / `New-VM` / `Move-VM` / `Set-VM` / `Start-VM` / `Stop-VM` / `New-Snapshot` 等高层 cmdlet，保证代码风格统一

---

## 📄 许可证

本项目基于 [MIT License](https://opensource.org/licenses/MIT) 开源。

```
Copyright (c) 2026 XuGuihong

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
- **Microsoft OWIN** —— 自托管 Web API 方案
- **所有测试用户** —— 生产环境反馈与改进建议