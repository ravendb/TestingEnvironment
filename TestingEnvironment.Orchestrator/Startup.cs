using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Nancy.Owin;

namespace TestingEnvironment.Orchestrator
{
    public class Startup
    {
        private readonly IConfiguration config;
        
        public Startup(IHostingEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional:false, reloadOnChange:true)
                .SetBasePath(env.ContentRootPath);

            config = builder.Build();            
        }
        
        public void Configure(IApplicationBuilder app)
        {
            var appConfig = new OrchestratorConfiguration();
            ConfigurationBinder.Bind(config, appConfig);

            app.UseOwin(x => x.UseNancy(opt => opt.Bootstrapper = new ConfigBootstrapper(appConfig)));
        }
    }
}
