using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Cors;
using VMwareOps.Core;

namespace VMwareOps.WebApi.Controllers
{
    [RoutePrefix("api/batch")]
    [EnableCors(origins: "*", headers: "*", methods: "*")]
    public class BatchController : ApiController
    {
        [HttpPost]
        [Route("power")]
        public async Task<IHttpActionResult> Power([FromBody] BatchPowerRequest req)
        {
            if (!SessionManager.Instance.IsConnected)
                return Content(System.Net.HttpStatusCode.Unauthorized, new { error = "not connected" });
            if (req == null || req.Ids == null || req.Ids.Count == 0)
                return BadRequest("ids required");
            if (string.IsNullOrEmpty(req.Action))
                return BadRequest("action required");

            var psAction = req.Action.ToLowerInvariant() switch
            {
                "on" => "Start-VM -Confirm:$false",
                "off" => "Stop-VM -Confirm:$false",
                "offhard" => "Stop-VM -Confirm:$false -Kill",
                "restart" => "Restart-VM -Confirm:$false",
                "suspend" => "Suspend-VM -Confirm:$false",
                "reset" => "Restart-VM -Confirm:$false -Force",
                _ => null
            };
            if (psAction == null)
                return BadRequest("action must be on/off/offhard/restart/suspend/reset");

            try
            {
                var ids = string.Join("','", req.Ids);
                var script = $@"
$ids = @('{ids}')
$result = @()
foreach ($id in $ids) {{
    $vm = Get-VM -Id $id -ErrorAction SilentlyContinue
    if ($vm) {{
        try {{
            {psAction} -VM $vm -ErrorAction Stop | Out-Null
            $result += [PSCustomObject]@{{ MoRef = $vm.ExtensionData.MoRef.Value; Name = $vm.Name; Success = $true }}
        }} catch {{
            $result += [PSCustomObject]@{{ MoRef = $vm.ExtensionData.MoRef.Value; Name = $vm.Name; Success = $false; Error = $_.Exception.Message }}
        }}
    }}
}}
$result
";
                var batch = await SessionManager.Instance.ExecuteQueryAsync(script);
                var success = batch.Count(b => (bool)b["Success"]);
                var fail = batch.Count - success;
                return Ok(new { total = batch.Count, success, fail, results = batch });
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }

        [HttpPost]
        [Route("snapshot")]
        public async Task<IHttpActionResult> Snapshot([FromBody] BatchSnapshotRequest req)
        {
            if (!SessionManager.Instance.IsConnected)
                return Content(System.Net.HttpStatusCode.Unauthorized, new { error = "not connected" });
            if (req == null || req.Ids == null || req.Ids.Count == 0)
                return BadRequest("ids required");
            if (string.IsNullOrEmpty(req.Action))
                return BadRequest("action required");

            try
            {
                var ids = string.Join("','", req.Ids);
                var script = req.Action.ToLowerInvariant() switch
                {
                    "create" => $@"
$ids = @('{ids}')
$result = @()
foreach ($id in $ids) {{
    $vm = Get-VM -Id $id -ErrorAction SilentlyContinue
    if ($vm) {{
        try {{
            New-Snapshot -VM $vm -Name '{req.Name}' -Description '{req.Description}' -Confirm:$false | Out-Null
            $result += [PSCustomObject]@{{ MoRef = $vm.ExtensionData.MoRef.Value; Name = $vm.Name; Success = $true }}
        }} catch {{
            $result += [PSCustomObject]@{{ MoRef = $vm.ExtensionData.MoRef.Value; Name = $vm.Name; Success = $false; Error = $_.Exception.Message }}
        }}
    }}
}}
$result",
                    "revert-latest" => $@"
$ids = @('{ids}')
$result = @()
foreach ($id in $ids) {{
    $vm = Get-VM -Id $id -ErrorAction SilentlyContinue
    if ($vm) {{
        try {{
            Get-Snapshot -VM $vm | Sort-Object Created -Descending | Select-Object -First 1 | Set-VM -Confirm:$false | Out-Null
            $result += [PSCustomObject]@{{ MoRef = $vm.ExtensionData.MoRef.Value; Name = $vm.Name; Success = $true }}
        }} catch {{
            $result += [PSCustomObject]@{{ MoRef = $vm.ExtensionData.MoRef.Value; Name = $vm.Name; Success = $false; Error = $_.Exception.Message }}
        }}
    }}
}}
$result",
                    "remove-all" => $@"
$ids = @('{ids}')
$result = @()
foreach ($id in $ids) {{
    $vm = Get-VM -Id $id -ErrorAction SilentlyContinue
    if ($vm) {{
        try {{
            Get-Snapshot -VM $vm | Remove-Snapshot -Confirm:$false | Out-Null
            $result += [PSCustomObject]@{{ MoRef = $vm.ExtensionData.MoRef.Value; Name = $vm.Name; Success = $true }}
        }} catch {{
            $result += [PSCustomObject]@{{ MoRef = $vm.ExtensionData.MoRef.Value; Name = $vm.Name; Success = $false; Error = $_.Exception.Message }}
        }}
    }}
}}
$result",
                    _ => throw new Exception("invalid action")
                };

                var batch = await SessionManager.Instance.ExecuteQueryAsync(script);
                var success = batch.Count(b => (bool)b["Success"]);
                var fail = batch.Count - success;
                return Ok(new { total = batch.Count, success, fail, results = batch });
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }
    }

    public class BatchPowerRequest
    {
        public List<string> Ids { get; set; } = new List<string>();
        public string Action { get; set; } = "";
    }

    public class BatchSnapshotRequest
    {
        public List<string> Ids { get; set; } = new List<string>();
        public string Action { get; set; } = "";
        public string Name { get; set; } = "Web_BatchSnapshot";
        public string Description { get; set; } = "";
    }
}