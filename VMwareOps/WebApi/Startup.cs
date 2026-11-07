using System;
using System.IO;
using System.Net.Http.Formatting;
using System.Web.Http;
using System.Web.Http.Cors;
using Owin;

namespace VMwareOps.WebApi
{
    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            var config = new HttpConfiguration();

            config.EnableCors();

            config.MapHttpAttributeRoutes();

            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional }
            );

            config.Formatters.Clear();
            config.Formatters.Add(new JsonMediaTypeFormatter
            {
                SerializerSettings = new Newtonsoft.Json.JsonSerializerSettings
                {
                    ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore,
                    NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore
                }
            });

            app.UseWebApi(config);

            ServeStaticFiles(app);
        }

        private void ServeStaticFiles(IAppBuilder app)
        {
            var wwwroot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WebApi", "wwwroot");
            if (!Directory.Exists(wwwroot)) return;

            app.Use(async (context, next) =>
            {
                var path = context.Request.Path.Value.TrimStart('/');
                if (string.IsNullOrEmpty(path)) path = "index.html";

                var filePath = Path.Combine(wwwroot, path);
                if (File.Exists(filePath))
                {
                    var ext = Path.GetExtension(filePath).ToLowerInvariant();
                    var mime = ext switch
                    {
                        ".html" => "text/html; charset=utf-8",
                        ".js" => "application/javascript; charset=utf-8",
                        ".css" => "text/css; charset=utf-8",
                        ".json" => "application/json; charset=utf-8",
                        ".svg" => "image/svg+xml",
                        ".png" => "image/png",
                        ".ico" => "image/x-icon",
                        _ => "application/octet-stream"
                    };

                    context.Response.ContentType = mime;
                    context.Response.StatusCode = 200;
                    using var stream = File.OpenRead(filePath);
                    await stream.CopyToAsync(context.Response.Body);
                    return;
                }

                await next();
            });
        }
    }
}