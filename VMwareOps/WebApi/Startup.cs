using System;
using System.IO;
using System.Net.Http.Formatting;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Cors;
using Owin;
using VMwareOps.Core;

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
            app.Use(async (context, next) =>
            {
                var path = context.Request.Path.Value.TrimStart('/');
                if (string.IsNullOrEmpty(path)) path = "index.html";

                if (await TryServeFromExtensions(context, path))
                    return;

                await next();
            });
        }

        private static async Task<bool> TryServeFromExtensions(dynamic context, string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            var enabled = ExtensionManager.Instance.GetEnabled();
            foreach (var ext in enabled)
            {
                var extDir = ExtensionManager.Instance.GetExtensionDir(ext.Id);
                if (!Directory.Exists(extDir)) continue;

                string relativePath;
                if (path.StartsWith("extensions/", StringComparison.OrdinalIgnoreCase))
                {
                    var remaining = path.Substring("extensions/".Length);
                    var slashIdx = remaining.IndexOf('/');
                    if (slashIdx >= 0)
                        relativePath = remaining.Substring(slashIdx + 1);
                    else
                        continue;
                }
                else
                {
                    relativePath = path;
                }

                if (string.IsNullOrEmpty(relativePath)) relativePath = "index.html";

                var candidate = Path.Combine(extDir, relativePath);
                if (!File.Exists(candidate)) continue;

                try
                {
                    var fullPath = Path.GetFullPath(candidate);
                    var fullExtDir = Path.GetFullPath(extDir).TrimEnd(Path.DirectorySeparatorChar);
                    if (!fullPath.StartsWith(fullExtDir, StringComparison.OrdinalIgnoreCase))
                        continue;
                }
                catch { continue; }

                context.Response.ContentType = GetMime(Path.GetExtension(candidate));
                context.Response.StatusCode = 200;
                using var stream = File.OpenRead(candidate);
                await stream.CopyToAsync(context.Response.Body);
                return true;
            }
            return false;
        }

        private static string GetMime(string ext) => ext.ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".htm" => "text/html; charset=utf-8",
            ".js" => "application/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".ico" => "image/x-icon",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            _ => "application/octet-stream"
        };
    }
}