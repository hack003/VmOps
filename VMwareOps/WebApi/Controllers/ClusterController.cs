using System;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Cors;
using VMwareOps.Core;

namespace VMwareOps.WebApi.Controllers
{
    [RoutePrefix("api/cluster")]
    [EnableCors(origins: "*", headers: "*", methods: "*")]
    public class ClusterController : ApiController
    {
        [HttpGet]
        [Route("")]
        public async Task<IHttpActionResult> List()
        {
            if (!SessionManager.Instance.IsConnected)
                return Content(System.Net.HttpStatusCode.Unauthorized, new { error = "not connected" });

            try
            {
                var script = @"Get-Cluster | ForEach-Object {
    [PSCustomObject]@{
        MoRef = $_.ExtensionData.MoRef.Value
        Name = $_.Name
        DrsEnabled = $_.Drs.Enabled
        HaEnabled = $_.HA.Enabled
        HostCount = (Get-VMHost -Location $_).Count
        VmCount = (Get-VM -Location $_).Count
    }
}";
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