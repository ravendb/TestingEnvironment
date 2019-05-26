using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

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

        // GET teagent/execute/ravendb
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
