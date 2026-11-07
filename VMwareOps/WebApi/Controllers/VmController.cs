using System;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Cors;
using VMwareOps.Core;

namespace VMwareOps.WebApi.Controllers
{
    [RoutePrefix("api/vm")]
    [EnableCors(origins: "*", headers: "*", methods: "*")]
    public class VmController : ApiController
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
                    ? @"Get-VM | ForEach-Object {
    [PSCustomObject]@{
        MoRef = $_.ExtensionData.MoRef.Value
        Name = $_.Name
        PowerState = $_.PowerState.ToString()
        GuestOS = $_.ExtensionData.Guest.OsFullName
        Host = $_.VMHost.Name
        NumCpu = $_.NumCpu
        MemoryGB = [math]::Round($_.MemoryMB / 1024, 1)
    }
}"
                    : $@"Get-VM | Where-Object {{ $_.Name -like '*{name}*' }} | ForEach-Object {{
    [PSCustomObject]{{
        MoRef = $_.ExtensionData.MoRef.Value
        Name = $_.Name
        PowerState = $_.PowerState.ToString()
        GuestOS = $_.ExtensionData.Guest.OsFullName
        Host = $_.VMHost.Name
        NumCpu = $_.NumCpu
        MemoryGB = [math]::Round($_.MemoryMB / 1024, 1)
    }}
}}";
                var result = await SessionManager.Instance.ExecuteQueryAsync(script);
                return Ok(result);
            }
            catch (Exception ex) { return InternalServerError(ex); }
        }

        [HttpGet]
        [Route("{id}/detail")]
        public async Task<IHttpActionResult> Detail(string id)
        {
            if (!SessionManager.Instance.IsConnected)
                return Content(System.Net.HttpStatusCode.Unauthorized, new { error = "not connected" });

            try
            {
                var script = $@"Get-VM -Id '{id}' | ForEach-Object {{
    [PSCustomObject]{{
        MoRef = $_.ExtensionData.MoRef.Value
        Name = $_.Name
        PowerState = $_.PowerState.ToString()
        GuestOS = $_.ExtensionData.Guest.OsFullName
        Host = $_.VMHost.Name
        Cluster = $_.VMHost.Parent.Name
        NumCpu = $_.NumCpu
        CpuCoreCount = $_.CpuCoreCount
        MemoryMB = $_.MemoryMB
        MemoryGB = [math]::Round($_.MemoryMB / 1024, 1)
        Notes = $_.Notes
    }}
}}";
                var result = await SessionManager.Instance.ExecuteQueryAsync(script);
                return Ok(result.FirstOrDefault());
            }
            catch (Exception ex) { return InternalServerError(ex); }
        }

        [HttpPost]
        [Route("{id}/power")]
        public async Task<IHttpActionResult> Power(string id, [FromBody] PowerRequest req)
        {
            if (!SessionManager.Instance.IsConnected)
                return Content(System.Net.HttpStatusCode.Unauthorized, new { error = "not connected" });
            if (req == null || string.IsNullOrEmpty(req.Action)) return BadRequest("action required");

            var action = req.Action.ToLowerInvariant() switch
            {
                "on" => "Start-VM -Confirm:$false",
                "off" => "Stop-VM -Confirm:$false",
                "offhard" => "Stop-VM -Confirm:$false -Kill",
                "restart" => "Restart-VM -Confirm:$false",
                "suspend" => "Suspend-VM -Confirm:$false",
                "reset" => "Restart-VM -Confirm:$false -Force",
                _ => null
            };
            if (action == null) return BadRequest("action invalid");

            try
            {
                var script = $"{action} -VM (Get-VM -Id '{id}') | Out-Null; Write-Output 'OK'";
                var result = await SessionManager.Instance.ExecutePsScriptAsync(script);
                return Ok(new { success = result.Contains("OK") });
            }
            catch (Exception ex) { return InternalServerError(ex); }
        }

        [HttpGet]
        [Route("{id}/snapshots")]
        public async Task<IHttpActionResult> Snapshots(string id)
        {
            if (!SessionManager.Instance.IsConnected)
                return Content(System.Net.HttpStatusCode.Unauthorized, new { error = "not connected" });
            try
            {
                var script = $@"Get-VM -Id '{id}' | Get-Snapshot | ForEach-Object {{
    [PSCustomObject]{{ Name = $_.Name; Description = $_.Description; Created = $_.Created; SizeGB = [math]::Round($_.SizeGB, 2) }}
}}";
                var result = await SessionManager.Instance.ExecuteQueryAsync(script);
                return Ok(result);
            }
            catch (Exception ex) { return InternalServerError(ex); }
        }

        [HttpPost]
        [Route("{id}/snapshots")]
        public async Task<IHttpActionResult> CreateSnapshot(string id, [FromBody] SnapshotRequest req)
        {
            if (!SessionManager.Instance.IsConnected)
                return Content(System.Net.HttpStatusCode.Unauthorized, new { error = "not connected" });
            if (req == null || string.IsNullOrEmpty(req.Name)) return BadRequest("name required");
            try
            {
                var desc = string.IsNullOrEmpty(req.Description) ? "" : $"-Description '{req.Description}'";
                var script = $"Get-VM -Id '{id}' | New-Snapshot -Name '{req.Name}' {desc} | Out-Null; Write-Output 'OK'";
                var result = await SessionManager.Instance.ExecutePsScriptAsync(script);
                return Ok(new { success = result.Contains("OK") });
            }
            catch (Exception ex) { return InternalServerError(ex); }
        }

        [HttpPost]
        [Route("{id}/snapshots/{name}/revert")]
        public async Task<IHttpActionResult> RevertSnapshot(string id, string name)
        {
            if (!SessionManager.Instance.IsConnected)
                return Content(System.Net.HttpStatusCode.Unauthorized, new { error = "not connected" });
            try
            {
                var script = $"Get-VM -Id '{id}' | Get-Snapshot -Name '{name}' | Set-VM -Confirm:$false | Out-Null; Write-Output 'OK'";
                var result = await SessionManager.Instance.ExecutePsScriptAsync(script);
                return Ok(new { success = result.Contains("OK") });
            }
            catch (Exception ex) { return InternalServerError(ex); }
        }

        [HttpDelete]
        [Route("{id}/snapshots/{name}")]
        public async Task<IHttpActionResult> DeleteSnapshot(string id, string name)
        {
            if (!SessionManager.Instance.IsConnected)
                return Content(System.Net.HttpStatusCode.Unauthorized, new { error = "not connected" });
            try
            {
                var script = $"Get-VM -Id '{id}' | Get-Snapshot -Name '{name}' | Remove-Snapshot -Confirm:$false | Out-Null; Write-Output 'OK'";
                var result = await SessionManager.Instance.ExecutePsScriptAsync(script);
                return Ok(new { success = result.Contains("OK") });
            }
            catch (Exception ex) { return InternalServerError(ex); }
        }

        [HttpGet]
        [Route("{id}/resources")]
        public async Task<IHttpActionResult> Resources(string id)
        {
            if (!SessionManager.Instance.IsConnected)
                return Content(System.Net.HttpStatusCode.Unauthorized, new { error = "not connected" });
            try
            {
                var script = $@"Get-VM -Id '{id}' | ForEach-Object {{
    [PSCustomObject]{{
        MoRef = $_.ExtensionData.MoRef.Value
        Name = $_.Name
        NumCpu = $_.NumCpu
        MemoryMB = $_.MemoryMB
        MemoryGB = [math]::Round($_.MemoryMB / 1024, 1)
        OS = $_.ExtensionData.Guest.OsFullName
        PowerState = $_.PowerState.ToString()
        Disks = (Get-HardDisk -VM $_ | ForEach-Object {{
            [PSCustomObject]@{{ Name = $_.Name; CapacityGB = [math]::Round($_.CapacityGB, 1); Datastore = ($_.Filename -split '\]')[0].TrimStart('[') }}
        }})
        NICs = (Get-NetworkAdapter -VM $_ | ForEach-Object {{
            [PSCustomObject]@{{ Name = $_.Name; Mac = $_.MacAddress; Network = $_.NetworkName; Connected = $_.ConnectionState.Connected }}
        }})
    }}
}}";
                var result = await SessionManager.Instance.ExecuteQueryAsync(script);
                return Ok(result.FirstOrDefault());
            }
            catch (Exception ex) { return InternalServerError(ex); }
        }

        [HttpPut]
        [Route("{id}/cpu")]
        public async Task<IHttpActionResult> SetCpu(string id, [FromBody] CpuRequest req)
        {
            if (!SessionManager.Instance.IsConnected)
                return Content(System.Net.HttpStatusCode.Unauthorized, new { error = "not connected" });
            if (req == null || req.Count <= 0) return BadRequest("count required");
            try
            {
                var script = $"Get-VM -Id '{id}' | Set-VM -NumCpu {req.Count} -Confirm:$false | Out-Null; Write-Output 'OK'";
                var result = await SessionManager.Instance.ExecutePsScriptAsync(script);
                return Ok(new { success = result.Contains("OK") });
            }
            catch (Exception ex) { return InternalServerError(ex); }
        }

        [HttpPut]
        [Route("{id}/memory")]
        public async Task<IHttpActionResult> SetMemory(string id, [FromBody] MemoryRequest req)
        {
            if (!SessionManager.Instance.IsConnected)
                return Content(System.Net.HttpStatusCode.Unauthorized, new { error = "not connected" });
            if (req == null || req.MemoryMB <= 0) return BadRequest("memoryMB required");
            try
            {
                var script = $"Get-VM -Id '{id}' | Set-VM -MemoryMB {req.MemoryMB} -Confirm:$false | Out-Null; Write-Output 'OK'";
                var result = await SessionManager.Instance.ExecutePsScriptAsync(script);
                return Ok(new { success = result.Contains("OK") });
            }
            catch (Exception ex) { return InternalServerError(ex); }
        }

        [HttpPost]
        [Route("{id}/disk")]
        public async Task<IHttpActionResult> AddDisk(string id, [FromBody] AddDiskRequest req)
        {
            if (!SessionManager.Instance.IsConnected)
                return Content(System.Net.HttpStatusCode.Unauthorized, new { error = "not connected" });
            if (req == null || req.CapacityGB <= 0) return BadRequest("capacityGB required");
            try
            {
                var ds = !string.IsNullOrEmpty(req.Datastore) ? $"-Datastore '{req.Datastore}'" : "";
                var script = $"Get-VM -Id '{id}' | New-HardDisk -CapacityGB {req.CapacityGB} {ds} -Persistence Persistent | Out-Null; Write-Output 'OK'";
                var result = await SessionManager.Instance.ExecutePsScriptAsync(script);
                return Ok(new { success = result.Contains("OK") });
            }
            catch (Exception ex) { return InternalServerError(ex); }
        }

        [HttpGet]
        [Route("templates")]
        public async Task<IHttpActionResult> Templates()
        {
            if (!SessionManager.Instance.IsConnected)
                return Content(System.Net.HttpStatusCode.Unauthorized, new { error = "not connected" });
            try
            {
                var script = @"Get-Template | ForEach-Object {
    [PSCustomObject]@{ MoRef = $_.ExtensionData.MoRef.Value; Name = $_.Name; GuestOS = $_.ExtensionData.Guest.OsFullName; Host = $_.VMHost.Name }
}";
                var result = await SessionManager.Instance.ExecuteQueryAsync(script);
                return Ok(result);
            }
            catch (Exception ex) { return InternalServerError(ex); }
        }
    }

    public class PowerRequest { public string Action { get; set; } }
    public class SnapshotRequest { public string Name { get; set; } public string Description { get; set; } }
    public class CpuRequest { public int Count { get; set; } }
    public class MemoryRequest { public int MemoryMB { get; set; } }
    public class AddDiskRequest { public int CapacityGB { get; set; } public string Datastore { get; set; } }
}