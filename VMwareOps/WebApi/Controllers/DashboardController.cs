using System;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Cors;
using VMwareOps.Core;

namespace VMwareOps.WebApi.Controllers
{
    [RoutePrefix("api/dashboard")]
    [EnableCors(origins: "*", headers: "*", methods: "*")]
    public class DashboardController : ApiController
    {
        [HttpGet]
        [Route("stats")]
        public async Task<IHttpActionResult> Stats()
        {
            if (!SessionManager.Instance.IsConnected)
                return Content(System.Net.HttpStatusCode.Unauthorized, new { error = "not connected" });

            try
            {
                var script = @"
$vms = Get-VM
$hosts = Get-VMHost
$clusters = Get-Cluster
$dss = Get-Datastore

$vmPoweredOn = ($vms | Where-Object { $_.PowerState -eq 'PoweredOn' }).Count
$vmPoweredOff = ($vms | Where-Object { $_.PowerState -eq 'PoweredOff' }).Count
$hostConnected = ($hosts | Where-Object { $_.ConnectionState -eq 'Connected' }).Count

$totalDsGB = [math]::Round(($dss | Measure-Object CapacityGB -Sum).Sum, 1)
$freeDsGB = [math]::Round(($dss | Measure-Object FreeSpaceGB -Sum).Sum, 1)
$usedDsPct = if ($totalDsGB -gt 0) { [math]::Round(($totalDsGB - $freeDsGB) / $totalDsGB * 100, 1) } else { 0 }

$totalCores = ($hosts | Measure-Object -Property ExtensionData.Hardware.CpuInfo.NumCpuCores -Sum).Sum
$totalMemGB = [math]::Round(($hosts | Measure-Object -Property ExtensionData.Hardware.MemorySize -Sum).Sum / 1GB, 1)

[PSCustomObject]@{
    TotalVMs = $vms.Count
    VMsPoweredOn = $vmPoweredOn
    VMsPoweredOff = $vmPoweredOff
    TotalHosts = $hosts.Count
    HostsConnected = $hostConnected
    TotalClusters = $clusters.Count
    TotalDatastores = $dss.Count
    TotalStorageGB = $totalDsGB
    FreeStorageGB = $freeDsGB
    StorageUsedPercent = $usedDsPct
    TotalCPUCores = $totalCores
    TotalMemoryGB = $totalMemGB
}
";
                var result = await SessionManager.Instance.ExecuteQueryAsync(script);
                return Ok(result[0]);
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }
    }
}