using System.Linq;
using System.Web.Http;
using System.Web.Http.Cors;
using VMwareOps.Core;

namespace VMwareOps.WebApi.Controllers
{
    [RoutePrefix("api/extensions")]
    [EnableCors(origins: "*", headers: "*", methods: "*")]
    public class ExtensionController : ApiController
    {
        private readonly ExtensionManager _mgr = ExtensionManager.Instance;

        [HttpGet]
        [Route("")]
        public IHttpActionResult List()
        {
            var list = _mgr.GetInstalled().Select(e => new
            {
                id = e.Id,
                name = e.Name,
                version = e.Version,
                enabled = e.Enabled,
                installedAt = e.InstalledAt
            }).ToList();
            return Ok(list);
        }

        [HttpGet]
        [Route("enabled")]
        public IHttpActionResult ListEnabled()
        {
            var list = _mgr.GetEnabled().Select(e => new
            {
                id = e.Id,
                name = e.Name,
                version = e.Version
            }).ToList();
            return Ok(list);
        }

        [HttpPost]
        [Route("{id}/enable")]
        public IHttpActionResult Enable(string id)
        {
            var ok = _mgr.SetEnabled(id, true);
            if (!ok) return NotFound();
            return Ok(new { success = true });
        }

        [HttpPost]
        [Route("{id}/disable")]
        public IHttpActionResult Disable(string id)
        {
            var ok = _mgr.SetEnabled(id, false);
            if (!ok) return NotFound();
            return Ok(new { success = true });
        }

        [HttpPost]
        [Route("install")]
        public async System.Threading.Tasks.Task<IHttpActionResult> Install([FromBody] InstallRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.Id) || string.IsNullOrEmpty(req.PackageUrl))
                return BadRequest("id and packageUrl required");

            var result = await _mgr.InstallAsync(req.Id, req.PackageUrl);
            if (!result.Success)
                return BadRequest(result.Error);

            return Ok(new { success = true, version = result.Manifest?.Version, name = result.Manifest?.Name });
        }

        [HttpDelete]
        [Route("{id}")]
        public IHttpActionResult Uninstall(string id)
        {
            var ok = _mgr.Uninstall(id);
            if (!ok) return NotFound();
            return Ok(new { success = true });
        }

        [HttpGet]
        [Route("market")]
        public async System.Threading.Tasks.Task<IHttpActionResult> Market([FromUri] string registry)
        {
            if (string.IsNullOrEmpty(registry))
                return BadRequest("registry url required");

            var list = await _mgr.FetchRegistryAsync(registry);
            return Ok(list);
        }
    }

    public class InstallRequest
    {
        public string Id { get; set; }
        public string PackageUrl { get; set; }
    }
}