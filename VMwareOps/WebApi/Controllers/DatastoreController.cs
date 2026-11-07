using System;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Cors;
using VMwareOps.Core;

namespace VMwareOps.WebApi.Controllers
{
    [RoutePrefix("api/datastore")]
    [EnableCors(origins: "*", headers: "*", methods: "*")]
    public class DatastoreController : ApiController
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
                    ? @"Get-Datastore | ForEach-Object {
    [PSCustomObject]@{
        Name = $_.Name
        Type = $_.Type
        TotalGB = [math]::Round($_.CapacityGB, 1)
        FreeGB = [math]::Round($_.FreeSpaceGB, 1)
        UsedGB = [math]::Round($_.CapacityGB - $_.FreeSpaceGB, 1)
        UsedPercent = [math]::Round(($_.CapacityGB - $_.FreeSpaceGB) / [math]::Max($_.CapacityGB, 1) * 100, 1)
    }
}"
                    : $@"Get-Datastore | Where-Object {{ $_.Name -like '*{name}*' }} | ForEach-Object {{
    [PSCustomObject]{{
        Name = $_.Name
        Type = $_.Type
        TotalGB = [math]::Round($_.CapacityGB, 1)
        FreeGB = [math]::Round($_.FreeSpaceGB, 1)
        UsedGB = [math]::Round($_.CapacityGB - $_.FreeSpaceGB, 1)
        UsedPercent = [math]::Round(($_.CapacityGB - $_.FreeSpaceGB) / [math]::Max($_.CapacityGB, 1) * 100, 1)
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
    }
}