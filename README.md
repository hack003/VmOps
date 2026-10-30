# 虚拟化运维工具.NET

基于 VMware vSphere PowerCLI 的虚拟化运维管理桌面工具，单 exe 绿色免安装。

## 功能概览

- vCenter 多会话连接管理（常驻通道 + 临时部署通道）
- 资源查询：集群、主机、虚拟机、数据存储、网络
- 虚拟机操作：电源管理、快照、资源调整、批量关机
- CSV 批量编辑：虚拟机名称/IP/规格批量修改
- OVF 模板部署：支持 CSV 批量部署（临时通道不阻塞主界面）
- 内置自动更新：检测 Gitee Release，下载替换运行中的 exe

## 技术栈

| 项目 | 说明 |
|------|------|
| 运行时 | .NET Framework 4.8 |
| UI | Windows Forms + DPI 自适应 |
| VMware 对接 | PowerCLI（Runspace 进程内宿主，双通道架构） |
| 单 exe 发布 | dotnet publish win-x64 + Costura.Fody 内嵌依赖 |
| 更新机制 | 独立 update_helper.exe 作为嵌入资源，运行时自替换 |

## 目录结构

`
VirtualizationOpsTool/
├── Core/            PowerShell 会话、缓存、部署管理器
├── UI/              MainForm + 各功能 Tab（Home / Query / OVF / CSV / Update）
├── Utils/           DpiUtils / Logger / Update 检测 / 版本号
├── VmOps/           电源 / 快照 / 资源操作
├── Resources/       图标、帮助文档、update_helper.exe（内嵌）
├── scripts/         Linux/Windows 来宾网络配置脚本
└── VirtualizationOpsTool.csproj
`

## 快速使用

1. 下载最新版本 exe：[Gitee Release](https://gitee.com/hack003/VmOps-Tool/releases)
2. 首次连接自动检测并安装 VMware PowerCLI 模块
3. 输入 vCenter 地址、用户名、密码 → 连接

## 源码编译

`ash
dotnet restore
dotnet build -c Release
dotnet publish -c Release -r win-x64 -p:SelfContained=false
`

打包脚本见根目录 uild_publish.bat（版本号递增 + 编译 + 备份）。

## 版本历史

版本号及更新日志见 Gitee Release 页面。

## 许可证

本项目仅供学习和技术交流使用。