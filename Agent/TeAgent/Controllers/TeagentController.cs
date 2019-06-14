using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using static TeAgent.TeAgentHelper;

namespace TeAgent.Controllers
{
    [Route("teagent/[controller]")]
    [ApiController]
    public class ExecuteController : ControllerBase
    {
        // GET api/values
        [HttpGet]
        public ActionResult<IEnumerable<string>> Get()
        {
            return new string[] { "value1", "value2" };
        }

        // GET teagent/execute/downloads?url=<url>&dest=<dest>
        [HttpGet("download")]
        public ActionResult DownloadHandler(string url, string dest, string extract)
        {
            var sp = Stopwatch.StartNew();
            var webClient = new WebClient();
            webClient.DownloadFile(url, dest);
            System.IO.Compression.ZipFile.ExtractToDirectory(dest, extract);
            return Content($"{url} downloaded to {dest} and extracted to {extract}. Took {sp.Elapsed}");
        }

        // GET teagent/execute/delete-dir?dir=<dirpath>
        [HttpGet("delete-dir")]
        public ActionResult DeleteDirHandler(string dir)
        {
            Directory.Delete(dir, true);
            return Content($"{dir} Deleted");
        }

        // GET teagent/execute/upgrade-ravendb?source=<src path>&dest=<dest path>&newdb=<true|false - copy db and settings.json>
        [HttpGet("upgrade-ravendb")]
        public ActionResult DeleteDirHandler(string source, string dest, bool newdb)
        {
            var sp = Stopwatch.StartNew();
            var procs = Process.GetProcessesByName("Raven.Server");
            if (procs.Length != 1)
            {
                HttpContext.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                return Content(
                    $"There are {procs.Length} Raven.Server processes running. Must have a single one in order to upgrade");
            }
            procs[0].Kill();

            if (newdb == false)
            {
                DirectoryCopy(Path.Combine(dest, "Server", "RavenData"), Path.Combine(source, "Server", "RavenData"),
                    true);
                DirectoryCopy(dest, source, false, "settings.json");
            }

            Directory.Delete(dest, true);
            DirectoryCopy(source, dest, true);

            var proc = new Process
            {
                StartInfo = new ProcessStartInfo(Path.Combine(dest, "Server", "Raven.Server"))
                {
                    WorkingDirectory = Path.Combine(dest, "Server")
                }
            };

            return Content($"RavenDB upgraded from {source} to {dest} with newdb={newdb}. Took {sp.Elapsed}, Proc={proc.Id}");
        }


        // GET teagent/execute/command?args=<ps1 script>
        [HttpGet("command")]
        public ActionResult Get(string args)
        {
            try
            {
                var sb = new StringBuilder();
                using (var ps = PowerShell.Create())
                {
                    var results = ps.AddScript(args).Invoke();
                    foreach (var result in results)
                    {
                        sb.Append($"{result.ToString()}<br>");
                    }

                    bool first = true;
                    foreach (var error in ps.Streams.Error)
                    {
                        if (first)
                        {
                            first = false;
                            sb.Append("<br>Errors:<br><br>");
                        }

                        sb.Append($"{error.FullyQualifiedErrorId} :: {error.ErrorDetails} | {error.Exception}<br>");
                    }
                }

                var response = @"<!DOCTYPE html>
                                <html>
                                <body>

                                <h1>Testing Environment</h1>
                                <h2>" + DateTime.Now + @"</h2>
                                <h3>" + args + @"</h3>
                                <h4>" + sb.ToString() + @"</h4>

                                </body>
                                </html>";
                return Content(response, "text/html");
            }
            catch (Exception e)
            {
                return Content("Exception thrown... : " + e, "text/plain");
            }
        }

        //// POST api/values
        //[HttpPost]
        //public void Post([FromBody] string value)
        //{
        //}

        //// PUT api/values/5
        //[HttpPut("{id}")]
        //public void Put(int id, [FromBody] string value)
        //{
        //}

        //// DELETE api/values/5
        //[HttpDelete("{id}")]
        //public void Delete(int id)
        //{
        //}
    }
}
