using System;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Cors;
using VMwareOps.Core;

namespace VMwareOps.WebApi.Controllers
{
    [RoutePrefix("api/host")]
    [EnableCors(origins: "*", headers: "*", methods: "*")]
    public class HostController : ApiController
    {
        [HttpGet]
        [Route("")]
        public async Task<IHttpActionResult> List([FromUri] string name = null)
        {
            if (!SessionManager.Instance.IsConnected)
                return Content(System.Net.HttpStatusCode.Unauthorized, new { error = "not connected" });

            try
            {
                var script = string.IsNullOrEmpty(name)
                    ? @"Get-VMHost | ForEach-Object {
    [PSCustomObject]@{
        MoRef = $_.ExtensionData.MoRef.Value
        Name = $_.Name
        PowerState = $_.PowerState.ToString()
        ConnectionState = $_.ConnectionState.ToString()
        ManagementIp = $_.ExtensionData.ManagementServerIp
        Cores = $_.ExtensionData.Hardware.CpuInfo.NumCpuCores
        Sockets = $_.ExtensionData.Hardware.CpuInfo.NumCpuPackages
        MemoryGB = [math]::Round($_.ExtensionData.Hardware.MemorySize / 1GB, 1)
        CpuUsagePct = $_.ExtensionData.Summary.GetCpuUsagePercentage()
        MemUsagePct = $_.ExtensionData.Summary.GetMemoryUsagePercentage()
        Cluster = $_.Parent.Name
        VmCount = (Get-VM -Location $_).Count
    }
}"
                    : $@"Get-VMHost | Where-Object {{ $_.Name -like '*{name}*' }} | ForEach-Object {{
    [PSCustomObject]{{
        MoRef = $_.ExtensionData.MoRef.Value
        Name = $_.Name
        PowerState = $_.PowerState.ToString()
        ConnectionState = $_.ConnectionState.ToString()
        ManagementIp = $_.ExtensionData.ManagementServerIp
        Cores = $_.ExtensionData.Hardware.CpuInfo.NumCpuCores
        Sockets = $_.ExtensionData.Hardware.CpuInfo.NumCpuPackages
        MemoryGB = [math]::Round($_.ExtensionData.Hardware.MemorySize / 1GB, 1)
        CpuUsagePct = $_.ExtensionData.Summary.GetCpuUsagePercentage()
        MemUsagePct = $_.ExtensionData.Summary.GetMemoryUsagePercentage()
        Cluster = $_.Parent.Name
        VmCount = (Get-VM -Location $_).Count
    }}
}}";

                var result = await SessionManager.Instance.ExecuteQueryAsync(script);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }

        [HttpGet]
        [Route("{id}/vms")]
        public async Task<IHttpActionResult> Vms(string id)
        {
            if (!SessionManager.Instance.IsConnected)
                return Content(System.Net.HttpStatusCode.Unauthorized, new { error = "not connected" });

            try
            {
                var script = $@"Get-VM -Location (Get-VMHost -Id '{id}') | ForEach-Object {{
    [PSCustomObject]{{
        MoRef = $_.ExtensionData.MoRef.Value
        Name = $_.Name
        PowerState = $_.PowerState.ToString()
        NumCpu = $_.NumCpu
        MemoryMB = $_.MemoryMB
    }}
}}";
                var result = await SessionManager.Instance.ExecuteQueryAsync(script);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }

        [HttpGet]
        [Route("{id}/detail")]
        public async Task<IHttpActionResult> Detail(string id)
        {
            if (!SessionManager.Instance.IsConnected)
                return Content(System.Net.HttpStatusCode.Unauthorized, new { error = "not connected" });

            try
            {
                var script = $@"Get-VMHost -Id '{id}' | ForEach-Object {{
    $hw = $_.ExtensionData.Hardware
    [PSCustomObject]@{{
        MoRef = $_.ExtensionData.MoRef.Value
        Name = $_.Name
        PowerState = $_.PowerState.ToString()
        ConnectionState = $_.ConnectionState.ToString()
        ManagementIp = $_.ExtensionData.ManagementServerIp
        Cores = $hw.CpuInfo.NumCpuCores
        Sockets = $hw.CpuInfo.NumCpuPackages
        CpuMhz = $hw.CpuInfo.Hz / 1000000
        MemoryGB = [math]::Round($hw.MemorySize / 1GB, 1)
        Model = $hw.SystemInfo.Model
        Vendor = $hw.SystemInfo.Vendor
        Cluster = $_.Parent.Name
        EsxVersion = $_.ExtensionData.Config.Product.Version
        Build = $_.ExtensionData.Config.Product.Build
    }}
}}";
                var result = await SessionManager.Instance.ExecuteQueryAsync(script);
                return Ok(result.FirstOrDefault());
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }
    }
}