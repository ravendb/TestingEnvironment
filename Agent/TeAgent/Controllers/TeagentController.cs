using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Channels;
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
            try
            {
                var sp = Stopwatch.StartNew();
                var webClient = new WebClient();
                webClient.DownloadFile(url, dest);
                System.IO.Compression.ZipFile.ExtractToDirectory(dest, extract);
                return Content($"{url} downloaded to {dest} and extracted to {extract}. Took {sp.Elapsed}");
            }
            catch (Exception e)
            {
                return Content("Exception thrown... : " + e, "text/plain");
            }
        }

        // GET teagent/execute/delete-dir?dir=<dirpath>
        [HttpGet("delete-dir")]
        public ActionResult DeleteDirHandler(string dir)
        {
            try
            {
                Directory.Delete(dir, true);
                return Content($"{dir} Deleted");
            }
            catch (Exception e)
            {
                return Content("Exception thrown... : " + e, "text/plain");
            }
        }

        // GET teagent/execute/upgrade-ravendb?extract=<src path>&dest=<dest path>&newdb=<true|false - copy db and settings.json>&args=[ravendb args, optional]
        [HttpGet("upgrade-ravendb")]
        public async Task<ActionResult> UpgradeRaven(string url, string extract, string dest, bool newdb, string args = null)
        {
            var sp = Stopwatch.StartNew();
            try
            {
                var webClient = new WebClient();
                if (System.IO.File.Exists("latest.zip"))
                    System.IO.File.Delete("latest.zip");
                webClient.DownloadFile(url, "latest.zip");
                try
                {
                    Directory.Delete(extract);
                }
                catch
                {

                }
                System.IO.Compression.ZipFile.ExtractToDirectory("latest.zip", extract, true);                
            }
            catch (Exception e)
            {
                return Content("Exception thrown... : " + e, "text/plain");
            }

            try
            {
                var procs = Process.GetProcessesByName("Raven.Server");
                if (procs.Length > 1)
                {
                    HttpContext.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                    return Content(
                        $"There are {procs.Length} Raven.Server processes running. Must have a single one in order to upgrade");
                }

                if (procs.Length != 0)
                    procs[0].Kill();

                Thread.Sleep(3000);

                if (newdb == false)
                {
                    DirectoryCopy(Path.Combine(dest, "Server", "RavenData"),
                        Path.Combine(extract, "Server", "RavenData"),
                        true);
                    DirectoryCopy(Path.Combine(dest, "Server"), Path.Combine(extract, "Server"), false, "settings.json");
                }

                try
                {
                    if (Directory.Exists(dest))
                        Directory.Delete(dest, true);
                    Directory.CreateDirectory(dest);
                }
                catch
                {
                    // ignore
                }

                try
                {
                    Directory.Move(extract, dest);
                }
                catch
                {
                    DirectoryCopy(extract, dest, true);
                }

                var proc = new Process
                {
                    StartInfo = new ProcessStartInfo(Path.Combine(dest, "Server", "Raven.Server"))
                    {
                        WorkingDirectory = Path.Combine(dest, "Server"),
                        UseShellExecute = false,
                        RedirectStandardOutput = true
                    }
                };
                if (args != null)
                    proc.StartInfo.Arguments = args;

                proc.Start();

                StreamReader reader = proc.StandardOutput;
                var sb = new StringBuilder();
                var cts = new CancellationTokenSource();
                //var t = new Task(() =>
                //{
                //    Stopwatch isp = Stopwatch.StartNew();
                //    while (true)
                //    {

                //        if (isp.Elapsed > TimeSpan.FromSeconds(10))
                //        {
                //            cts.Cancel();
                //            break;
                //        }
                //    }
                //});

                //t.Start();

                while (true)
                {
                    var buffer = new char[4096];
                    var byteCount = reader.ReadAsync(buffer, cts.Token).Result; // TODO: cancel doesn't work
                    if (cts.IsCancellationRequested)
                        break;

                    sb.Append(buffer,0, byteCount);
                    if (sb.ToString().Contains("Server started"))
                        break;
                }

                return Content(
                    $"RavenDB upgraded from {extract} to {dest} with newdb={newdb}. Took {sp.Elapsed}, Proc={proc.Id}, Cancel={cts.IsCancellationRequested}, Result={sb}");
            }
            catch (Exception e)
            {
                var info = $"({extract} to {dest} with newdb={newdb}) ";
                return Content($"{info}Exception thrown... : {e}", "text/plain");
            }
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
