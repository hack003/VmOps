using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Cors;
using VMwareOps.Core;

namespace VMwareOps.WebApi.Controllers
{
    [RoutePrefix("api/session")]
    [EnableCors(origins: "*", headers: "*", methods: "*")]
    public class SessionController : ApiController
    {
        [HttpGet]
        [Route("status")]
        public IHttpActionResult GetStatus()
        {
            var s = SessionManager.Instance;
            return Ok(new
            {
                connected = s.IsConnected,
                vc = s.CurrentVc,
                domain = s.CurrentDomain,
                taskRunning = s.IsTaskRunning
            });
        }

        [HttpPost]
        [Route("connect")]
        public async Task<IHttpActionResult> Connect([FromBody] ConnectRequest req)
        {
            if (req == null || string.IsNullOrEmpty(req.Vc) || string.IsNullOrEmpty(req.User))
                return BadRequest("vc and user are required");

            try
            {
                var ok = await SessionManager.Instance.ConnectAsync(
                    req.Vc, req.User, req.Password, req.Domain);

                if (ok)
                    return Ok(new { success = true, vc = SessionManager.Instance.CurrentVc });
                else
                    return BadRequest("连接失败");
            }
            catch (System.Exception ex)
            {
                return InternalServerError(ex);
            }
        }

        [HttpPost]
        [Route("disconnect")]
        public async Task<IHttpActionResult> Disconnect()
        {
            await SessionManager.Instance.DisconnectAsync();
            return Ok(new { success = true });
        }
    }

    public class ConnectRequest
    {
        public string Vc { get; set; }
        public string User { get; set; }
        public string Password { get; set; }
        public string Domain { get; set; }
    }
}