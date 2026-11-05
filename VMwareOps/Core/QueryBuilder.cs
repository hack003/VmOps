// ============================================================================
// QueryBuilder.cs - PowerCLI 脚本片段构建器（查询 + 详情）
// ============================================================================
// 职责：根据资源类型（vm/host/datastore/network/template）构建对应 PowerShell 脚本
//
// 核心方法：
//   ✅ GetJsonQueryScript(resourceType) — 列表查询脚本 → 委托 TableConvertUtils 对应方法
//   ✅ BuildDetailQuery(resourceType, targetId, targetName) — 单对象详情查询脚本
//
// 约束：
//   所有详情查询脚本统一 Get-View + VIM API，禁用 Get-VM/Get-VMHost 等高级 cmdlet
//   -ErrorAction Stop 必须加，vCenter 操作全在 Runspace 全局作用域执行
// ============================================================================
using System.Collections.Generic;
using VMwareOps.Utils;

namespace VMwareOps.Core
{
    public static class QueryBuilder
    {
        public static string GetJsonQueryScript(string resourceType, Dictionary<string, object>? extra = null)
        {
            return resourceType switch
            {
                "vm" => TableConvertUtils.QueryScriptVmList(),
                "host" => TableConvertUtils.QueryScriptHostList(),
                "datastore" => TableConvertUtils.QueryScriptDatastoreList(),
                "network" => TableConvertUtils.QueryScriptNetworkList(),
                "network_standard_pg" => TableConvertUtils.QueryScriptNetworkStandardPg(),
                "network_distributed_pg" => TableConvertUtils.QueryScriptNetworkDistributedPg(),
                "network_vdswitch" => TableConvertUtils.QueryScriptNetworkVdswitch(),
                "template" => TableConvertUtils.QueryScriptTemplateList(),
                "vm_ip" => TableConvertUtils.QueryScriptGuestIpList(),
                _ => ""
            };
        }

        public static (string script, string titlePrefix) BuildDetailQuery(string resourceType, string targetId, string targetName = "")
        {
            string escId = (targetId ?? "").Replace("'", "''");
            string escName = (targetName ?? "").Replace("'", "''");
            string targetLabel = !string.IsNullOrEmpty(escName) ? escName : escId;

            if (resourceType == "vm" || resourceType == "template")
            {
                string title = resourceType == "vm" ? "虚拟机详细信息" : "模板详细信息";
                string script;
                if (resourceType == "template")
                {
                    script = $@"
$ErrorActionPreference = 'Stop';
try {{
$tpl = Get-View -Id '{escId}' -Property Name,Config.CreateDate,Config.Hardware,Config.Annotation -ErrorAction Stop;
if (-not $tpl) {{ Write-Output '【错误】未找到模板：{targetLabel}'; return }};
Write-Output '=== 基本信息 ===';
Write-Output ('  名称：' + $tpl.Name);
Write-Output ('  完整 ID：' + $tpl.MoRef.Type + '-' + $tpl.MoRef.Value);
$ct = '--';
try {{ if ($tpl.Config.CreateDate) {{ $ct = $tpl.Config.CreateDate.ToString('yyyy/MM/dd HH:mm:ss') }} }} catch {{}};
Write-Output ('  创建时间：' + $ct);
Write-Output ('  CPU 数量：' + $tpl.Config.Hardware.NumCPU);
Write-Output ('  内存 (GB)：' + [math]::Round($tpl.Config.Hardware.MemoryMB/1024, 2));
Write-Output ('  备注：' + $(if($tpl.Config.Annotation){{$tpl.Config.Annotation}}else{{'(无)'}}));
}} catch {{ Write-Output '【错误】详情查询失败：' + $_.Exception.Message }}
";
                }
                else
                {
                    script = $@"
$ErrorActionPreference = 'Stop';
try {{
$vm = Get-View -Id '{escId}' -Property Name,Config.Hardware.NumCPU,Config.Hardware.MemoryMB,Config.Hardware.Device,Config.GuestFullName,Config.Annotation,Config.Uuid,Config.Version,Config.Files.VmPathName,Config.CreateDate,Runtime.PowerState,Runtime.Host,OverallStatus,Guest,ResourcePool,Snapshot -ErrorAction Stop;
if (-not $vm) {{ Write-Output '【错误】未找到虚拟机：{targetLabel}'; return }};
$guest = $vm.Guest; $hw = $vm.Config.Hardware;
$hostName = '(无)'; $clusterName = '(无)'; $dcName = '(无)';
if ($vm.Runtime.Host) {{
  $hostView = Get-View -Id $vm.Runtime.Host -Property Name,Parent -ErrorAction SilentlyContinue;
  if ($hostView) {{ $hostName = $hostView.Name }};
  if ($hostView -and $hostView.Parent) {{
    $parentView = Get-View -Id $hostView.Parent -Property Name,Parent -ErrorAction SilentlyContinue;
    if ($parentView) {{
      $clusterName = $parentView.Name;
      $cur = $parentView;
      while ($cur -and $cur.Parent) {{
        $cur = Get-View -Id $cur.Parent -Property Name,Parent -ErrorAction SilentlyContinue;
        if ($cur -and $cur.GetType().Name -eq 'Datacenter') {{ $dcName = $cur.Name; break }};
      }};
    }};
  }};
}};
$guestOs = if($guest -and $guest.GuestFullName) {{ $guest.GuestFullName }} elseif($vm.Config.GuestFullName) {{ $vm.Config.GuestFullName }} else {{ '(未检测到)' }};
$annotation = if($vm.Config.Annotation) {{ $vm.Config.Annotation }} else {{ '(无)' }};
$vcVmId = $vm.MoRef.Value;
Write-Output '=== 基本信息 ===';
Write-Output ('  名称：' + $vm.Name); Write-Output ('  VM MoRef: ' + $vm.MoRef.Type + '-' + $vcVmId); Write-Output ('  VM ID: ' + $vcVmId);
Write-Output ('  UUID: ' + $vm.Config.Uuid);
Write-Output ('  电源状态: ' + $vm.Runtime.PowerState); Write-Output ('  所在主机: ' + $hostName);
Write-Output ('  所在集群: ' + $clusterName); Write-Output ('  所在数据中心: ' + $dcName);
Write-Output ('  硬件版本: ' + $vm.Config.Version); Write-Output ('  客户机操作系统: ' + $guestOs);
Write-Output ('  VMware Tools 状态: ' + $(if($guest){{$guest.ToolsStatus}}else{{'(无)'}})); Write-Output ('  VMware Tools 版本: ' + $(if($guest -and $guest.ToolsVersion){{$guest.ToolsVersion}}else{{'(无)'}}));
Write-Output ('  整体状态: ' + $vm.OverallStatus); Write-Output ('  CPU 数量: ' + $vm.Config.Hardware.NumCPU);
Write-Output ('  内存 (GB): ' + [math]::Round($vm.Config.Hardware.MemoryMB/1024,2));
$dsName = '(无)'; if ($vm.Config.Files.VmPathName -match '^\[([^\]]+)\]') {{ $dsName = $matches[1] }};
Write-Output ('  所在存储: ' + $dsName);
$rpName = '(无)'; if ($vm.ResourcePool) {{ $rpView = Get-View -Id $vm.ResourcePool -Property Name -ErrorAction SilentlyContinue; if ($rpView) {{ $rpName = $rpView.Name }} }};
Write-Output ('  资源池: ' + $rpName);
$createDate = '(未知)'; if ($vm.Config.CreateDate) {{ $createDate = $vm.Config.CreateDate.ToString('yyyy/MM/dd HH:mm:ss') }};
Write-Output ('  创建时间: ' + $createDate); Write-Output ('  注释: ' + $annotation); Write-Output '';
Write-Output '=== 网络信息 ===';
$nicList = @();
foreach ($dev in $vm.Config.Hardware.Device) {{
  if ($dev -is [VMware.Vim.VirtualEthernetCard]) {{
    $netName = '(未连接)';
    if ($dev.Backing) {{
      if ($dev.Backing -is [VMware.Vim.VirtualEthernetCardNetworkBackingInfo]) {{
        if ($dev.Backing.NetworkName) {{ $netName = $dev.Backing.NetworkName }}
        elseif ($dev.Backing.Network) {{ $nv = Get-View -Id $dev.Backing.Network -Property Name -ErrorAction SilentlyContinue; if ($nv) {{ $netName = $nv.Name }} }}
      }} elseif ($dev.Backing -is [VMware.Vim.VirtualEthernetCardDistributedVirtualPortBackingInfo]) {{
        try {{
          $pgKey = $dev.Backing.Port.PortgroupKey;
          if ($pgKey) {{
            $pg = Get-View -ViewType DistributedVirtualPortgroup -Filter @{{Key=$pgKey}} -Property Name -ErrorAction SilentlyContinue;
            if ($pg) {{ $netName = $pg.Name }}
          }}
        }} catch {{}}
      }} elseif ($dev.Backing -is [VMware.Vim.VirtualEthernetCardOpaqueNetworkBackingInfo]) {{
        if ($dev.Backing.OpaqueNetworkId) {{ $netName = $dev.Backing.OpaqueNetworkId }}
      }} else {{
        try {{ if ($dev.Backing.psobject.Properties['NetworkName'] -and $dev.Backing.NetworkName) {{ $netName = $dev.Backing.NetworkName }} }} catch {{}}
      }}
    }};
    $conn = '(未知)';
    if ($dev.Connectable) {{
      if ($dev.Connectable.Connected) {{ $conn = '是' }} else {{ $conn = '否' }};
    }};
    $nicType = $dev.GetType().Name -replace 'Virtual','';
    $nicList += [PSCustomObject]@{{'名称'=$dev.DeviceInfo.Label;'网络名'=$netName;'MAC 地址'=$dev.MacAddress;'连接状态'=$conn;'类型'=$nicType}};
  }};
}};
if ($nicList.Count -gt 0) {{
  $nicList | ConvertTo-Json -Compress -Depth 2;
}} else {{ Write-Output '(无网卡)' }};
Write-Output ''; Write-Output '=== 客户机 IP 地址 (IPv4) ==='; $foundIp=$false; $ipIdx=0;
if ($guest -and $guest.Net) {{ foreach ($gn in $guest.Net) {{ if ($gn.IpAddress) {{ foreach ($ip in $gn.IpAddress) {{ if ($ip -match '^\d{{1,3}}\.\d{{1,3}}\.\d{{1,3}}\.\d{{1,3}}$') {{ $foundIp=$true; $ipIdx++; Write-Output ('IPv4 地址' + $(if($ipIdx -gt 1){{' '+$ipIdx}}else{{}}) + ': ' + $ip.Trim()) }} }} }} }} }};
if (-not $foundIp -and $guest -and $guest.IpAddress) {{ foreach ($ip in $guest.IpAddress) {{ if ($ip -match '^\d{{1,3}}\.\d{{1,3}}\.\d{{1,3}}\.\d{{1,3}}$') {{ $foundIp=$true; $ipIdx++; Write-Output ('IPv4 地址' + $(if($ipIdx -gt 1){{' '+$ipIdx}}else{{}}) + ': ' + $ip.Trim()) }} }} }};
if (-not $foundIp) {{ Write-Output 'IPv4 地址: (未检测到有效的 IPv4 地址，可能处于关机状态、未安装 VMware Tools 或仅配置了 IPv6)' }};
Write-Output ''; Write-Output '=== 磁盘信息 ===';
$disks = @(); foreach ($dev in $hw.Device) {{ if ($dev.DeviceInfo -and $dev.GetType().FullName -match 'VirtualDisk') {{ $disks += $dev }} }};
if ($disks.Count -gt 0) {{ $diskRows = New-Object System.Collections.ArrayList;
  foreach ($d in $disks) {{ $ds='(未知)'; $dm='(未知)';
    if ($d.Backing) {{ if ($d.Backing.psobject.Properties['FileName']) {{ $fn=$d.Backing.FileName; if ($fn -match '^\[([^\]]+)\]') {{ $ds = $matches[1] }} }}; if ($d.Backing.psobject.Properties['DiskMode']) {{ $dm=$d.Backing.DiskMode }} }};
    [void]$diskRows.Add([PSCustomObject]@{{'名称'=$d.DeviceInfo.Label;'容量 (GB)'=[math]::Round($d.CapacityInKB/1048576,2);'持久化模式'=$dm;'数据存储'=$ds}}); }};
  $diskRows | ConvertTo-Json -Compress -Depth 2; }} else {{ Write-Output '(无磁盘)' }};
Write-Output ''; Write-Output '=== 光驱信息 ===';
$cds = @(); foreach ($dev in $hw.Device) {{ if ($dev.DeviceInfo -and $dev.GetType().FullName -match 'VirtualCdrom') {{ $cds += $dev }} }};
if ($cds.Count -gt 0) {{ $cdRows = New-Object System.Collections.ArrayList;
  foreach ($cd in $cds) {{ $iso='(客户端设备)'; if ($cd.Backing -and $cd.Backing.psobject.Properties['IsoPath'] -and $cd.Backing.IsoPath) {{ $iso=$cd.Backing.IsoPath }}; $conn = if($cd.Connectable -and $cd.Connectable.Connected){{'是'}}else{{'否'}};
    [void]$cdRows.Add([PSCustomObject]@{{'名称'=$cd.DeviceInfo.Label;'已连接'=$conn;'ISO 路径'=$iso}}); }};
  $cdRows | ConvertTo-Json -Compress -Depth 2; }} else {{ Write-Output '(无光驱)' }};
Write-Output ''; Write-Output '=== 快照信息 ===';
$snapTree = $vm.Snapshot;
if ($snapTree -and $snapTree.RootSnapshotList) {{ $snapRows = New-Object System.Collections.ArrayList;
  function _walkSnap($list) {{ foreach ($s in $list) {{ $curr = if($snapTree.CurrentSnapshot -and $s -eq $snapTree.CurrentSnapshot){{'*是'}}else{{''}};
    [void]$snapRows.Add([PSCustomObject]@{{'名称'=$s.Name;'描述'=$(if($s.Description){{$s.Description}}else{{''}});'创建时间'=$s.CreateTime.ToString('yyyy/MM/dd HH:mm:ss');'当前'=$curr}}); _walkSnap $s.ChildSnapshotList }} }};
  _walkSnap $snapTree.RootSnapshotList;
  if ($snapRows.Count -gt 0) {{ $snapRows | ConvertTo-Json -Compress -Depth 2 }} else {{ Write-Output '(无快照)' }}; }} else {{ Write-Output '(无快照)' }};
}} catch {{ Write-Output '【错误】详情查询失败：' + $_.Exception.Message }}
";
                }
                return (script, title);
            }
            else if (resourceType == "host")
            {
                string title = "主机详细信息";
                string script = $@"
$ErrorActionPreference = 'Stop';
try {{
$hostObj = Get-View -Id '{escId}' -Property Name,Hardware,Runtime,Summary.QuickStats,Config.Product.Version,Config.Product.Build,Config.Network -ErrorAction Stop;
if (-not $hostObj) {{ Write-Output '【错误】未找到主机：{targetLabel}'; return }};
$hw = $hostObj.Hardware;
$cpuInfo = if ($hw) {{ $hw.CpuInfo }} else {{ $null }};
$numCpu = if ($cpuInfo) {{ $cpuInfo.NumCpuCores }} else {{ 0 }};
$cpuTotalMhz = if ($cpuInfo -and $cpuInfo.Hz -gt 0) {{ [math]::Round($cpuInfo.Hz / 1000000 * $cpuInfo.NumCpuCores) }} else {{ 0 }};
$memTotalGB = if ($hw) {{ [math]::Round($hw.MemorySize / 1073741824, 2) }} else {{ 0 }};
$qs = if ($hostObj.Summary) {{ $hostObj.Summary.QuickStats }} else {{ $null }};
$memUsedMB = if ($qs) {{ $qs.OverallMemoryUsage }} else {{ 0 }};
$memUsedGB = [math]::Round($memUsedMB / 1024.0, 2);
$cpuPct = if ($qs -and $cpuTotalMhz -gt 0) {{ [math]::Round($qs.OverallCpuUsage / $cpuTotalMhz * 100, 1) }} else {{ 0 }};
$connState = '--'; try {{ if ($hostObj.Runtime) {{ $connState = $hostObj.Runtime.ConnectionState.ToString() }} }} catch {{}};
$vendor = if ($hw -and $hw.SystemInfo) {{ $hw.SystemInfo.Vendor }} else {{ '--' }};
$model = if ($hw -and $hw.SystemInfo) {{ $hw.SystemInfo.Model }} else {{ '--' }};
$cpuDesc = '--'; try {{ if ($hw -and $hw.CpuPkg -and $hw.CpuPkg.Count -gt 0 -and $hw.CpuPkg[0].Description) {{ $cpuDesc = $hw.CpuPkg[0].Description }} }} catch {{}};
$biosVer = if ($hw -and $hw.BiosInfo) {{ $hw.BiosInfo.BiosVersion }} else {{ '--' }};
$productVer = if ($hostObj.Config -and $hostObj.Config.Product) {{ $hostObj.Config.Product.Version }} else {{ '--' }};
$productBuild = if ($hostObj.Config -and $hostObj.Config.Product) {{ $hostObj.Config.Product.Build }} else {{ '--' }};
$mgmtIp = '(未获取到)';
try {{
    $vnicList = @();
    if ($hostObj.Config -and $hostObj.Config.Network -and $hostObj.Config.Network.Vnic) {{
        $vnicList = $hostObj.Config.Network.Vnic
    }};
    $mgmtFound = $false;
    foreach ($vn in $vnicList) {{
        $ip = $vn.Spec.Ip.IpAddress;
        $pg = $vn.Portgroup;
        if ($ip -and $ip -match '^\d+\.\d+\.\d+\.\d+$' -and -not $ip.StartsWith('127.') -and -not $ip.StartsWith('169.254.')) {{
            if ($pg -match 'Management' -or -not $mgmtFound) {{
                $mgmtIp = $ip; $mgmtFound = $true
            }}
        }}
    }}
}} catch {{ $mgmtIp = '(获取失败)' }};
Write-Output '=== 基本信息 ===';
Write-Output ('  名称：' + $hostObj.Name);
Write-Output ('  完整 ID：' + $hostObj.MoRef.Type + '-' + $hostObj.MoRef.Value);
Write-Output ('  管理 IP：' + $mgmtIp);
Write-Output ('  状态：' + $connState);
Write-Output ('  制造商：' + $vendor);
Write-Output ('  型号：' + $model);
Write-Output ('  CPU 型号：' + $cpuDesc);
Write-Output ('  CPU 核心数：' + $numCpu);
Write-Output ('  总内存 (GB)：' + $memTotalGB);
Write-Output ('  已用内存 (GB)：' + $memUsedGB);
Write-Output ('  CPU 使用率 (%)：' + $cpuPct);
Write-Output ('  BIOS 版本：' + $biosVer);
Write-Output ('  版本：' + $productVer);
Write-Output ('  构建号：' + $productBuild);
}} catch {{ Write-Output '【错误】详情查询失败：' + $_.Exception.Message }}
";
                return (script, title);
            }
            else if (resourceType == "datastore")
            {
                string title = "存储详细信息";
                string script = $@"
$ErrorActionPreference = 'Stop';
try {{
$ds = Get-View -Id '{escId}' -Property Name,Summary,Info,Host -ErrorAction Stop;
if (-not $ds) {{ Write-Output '【错误】未找到存储：{targetLabel}'; return }};
Write-Output '=== 基本信息 ===';
Write-Output ('  名称：' + $ds.Name);
Write-Output ('  完整 ID：' + $ds.MoRef.Type + '-' + $ds.MoRef.Value);
Write-Output ('  类型：' + $ds.Summary.Type);
$cap = if ($ds.Summary) {{ $ds.Summary.Capacity }} else {{ 0 }};
$free = if ($ds.Summary) {{ $ds.Summary.FreeSpace }} else {{ 0 }};
Write-Output ('  容量 (GB)：' + [math]::Round($cap/1073741824, 2));
Write-Output ('  空闲 (GB)：' + [math]::Round($free/1073741824, 2));
$used = if ($cap -gt 0) {{ [math]::Round(($cap - $free) / $cap * 100, 1) }} else {{ 0 }};
Write-Output ('  使用率：' + $used + '%');
Write-Output ('  可访问：' + $ds.Summary.Accessible);
Write-Output ('  文件系统版本：' + $ds.Summary.FilesystemVersion);
$hostCount = 0;
try {{ if ($ds.Host) {{ $hostCount = $ds.Host.Count }} }} catch {{}};
Write-Output '';
Write-Output '=== 关联主机 ===';
Write-Output ('  挂载主机数：' + $hostCount);
if ($ds.Host) {{
  $hostList = Get-View -Id ($ds.Host | ForEach-Object {{ $_.Key }}) -Property Name -ErrorAction SilentlyContinue;
  foreach ($h in $hostList) {{ Write-Output ('  - ' + $h.Name) }}
}} else {{ Write-Output '  (无)' }}
}} catch {{ Write-Output '【错误】详情查询失败：' + $_.Exception.Message }}
";
                return (script, title);
            }
            else if (resourceType == "network")
            {
                string title = "网络详细信息";
                string script = $@"
$ErrorActionPreference = 'Stop';
try {{
$net = Get-View -Id '{escId}' -ErrorAction Stop;
if (-not $net) {{ Write-Output '【错误】未找到网络：{targetLabel}'; return }};
$typeName = $net.GetType().Name;
if ($typeName -eq 'DistributedVirtualPortgroup') {{
  $vlan = '--';
  try {{
    $vlanSpec = $net.Config.DefaultPortConfig.Vlan;
    if ($vlanSpec) {{
      if ($vlanSpec -is [VMware.Vim.VmwareDistributedVirtualSwitchVlanIdSpec]) {{ $vlan = $vlanSpec.VlanId }}
      elseif ($vlanSpec -is [VMware.Vim.VmwareDistributedVirtualSwitchTrunkVlanSpec]) {{ $vlan = 'Trunk' }}
      elseif ($vlanSpec -is [VMware.Vim.VmwareDistributedVirtualSwitchPvlanSpec]) {{ $vlan = ""PVLAN-$($vlanSpec.PvlanId)"" }}
    }}
  }} catch {{}};
  $numPorts = if ($net.Config.NumPorts) {{ $net.Config.NumPorts }} else {{ '--' }};
  Write-Output '=== 基本信息 ===';
  Write-Output ('  名称：' + $net.Name);
  Write-Output ('  完整 ID：' + $net.MoRef.Type + '-' + $net.MoRef.Value);
  Write-Output ('  类型：分布式端口组');
  Write-Output ('  VLAN ID：' + $vlan);
  Write-Output ('  端口数：' + $numPorts);
  Write-Output '';
  Write-Output '=== 关联主机 ===';
  $hostCount = 0;
  try {{ if ($net.Host) {{ $hostCount = $net.Host.Count }} }} catch {{}};
  Write-Output ('  关联主机数：' + $hostCount);
  if ($net.Host -and $hostCount -gt 0) {{
    $hostList = Get-View -Id $net.Host -Property Name -ErrorAction SilentlyContinue;
    foreach ($h in $hostList) {{ Write-Output ('  - ' + $h.Name) }}
  }} else {{ Write-Output '  (无)' }}
}} elseif ($typeName -eq 'Network') {{
  $stdPgHosts = @();
  Get-View -ViewType HostSystem -Property Name,Config.Network -ErrorAction SilentlyContinue | ForEach-Object {{
    if ($_.Config.Network -and $_.Config.Network.Portgroup) {{
      foreach ($pg in $_.Config.Network.Portgroup) {{
        if ($pg.Spec.Name -eq $net.Name) {{ $stdPgHosts += $_.Name }}
      }}
    }}
  }};
  $vlan = '--';
  try {{
    Get-View -ViewType HostSystem -Property Config.Network.Portgroup -ErrorAction SilentlyContinue | ForEach-Object {{
      foreach ($pg in $_.Config.Network.Portgroup) {{
        if ($pg.Spec.Name -eq $net.Name) {{ if ($pg.Spec.VlanId -ne $null -and $pg.Spec.VlanId -ne '') {{ $vlan = $pg.Spec.VlanId }}; break }}
      }}
    }}
  }} catch {{}};
  Write-Output '=== 基本信息 ===';
  Write-Output ('  名称：' + $net.Name);
  Write-Output ('  完整 ID：' + $net.MoRef.Type + '-' + $net.MoRef.Value);
  Write-Output ('  类型：标准端口组');
  Write-Output ('  VLAN ID：' + $vlan);
  Write-Output '';
  Write-Output '=== 关联主机 ===';
  Write-Output ('  关联主机数：' + $stdPgHosts.Count);
  if ($stdPgHosts.Count -gt 0) {{
    foreach ($hn in $stdPgHosts) {{ Write-Output ('  - ' + $hn) }}
  }} else {{ Write-Output '  (无)' }}
}} elseif ($typeName -eq 'DistributedVirtualSwitch') {{
  Write-Output '=== 基本信息 ===';
  Write-Output ('  名称：' + $net.Name);
  Write-Output ('  完整 ID：' + $net.MoRef.Type + '-' + $net.MoRef.Value);
  Write-Output ('  类型：分布式交换机');
}} else {{
  Write-Output ('【错误】未知网络对象类型：' + $typeName);
}}
}} catch {{ Write-Output '【错误】详情查询失败：' + $_.Exception.Message }}
";
                return (script, title);
            }

            return ("", "未知类型");
        }
    }
}