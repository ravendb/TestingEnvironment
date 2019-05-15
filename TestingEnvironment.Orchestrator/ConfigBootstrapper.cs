using Nancy;
using Nancy.Configuration;
using Nancy.TinyIoc;

namespace TestingEnvironment.Orchestrator
{
    public class ConfigBootstrapper : DefaultNancyBootstrapper
    {
        private readonly OrchestratorConfiguration _configuration;

        public ConfigBootstrapper()
        {
        }

        public ConfigBootstrapper(OrchestratorConfiguration configuration)
        {
            _configuration = configuration;
        }

        public override void Configure(INancyEnvironment environment)
        {
            base.Configure(environment);
            environment.Tracing(true, true); //enable showing error page for unhandled server exceptions
        }

        protected override void ConfigureApplicationContainer(TinyIoCContainer container)
        {
            base.ConfigureApplicationContainer(container);
            container.Register(_configuration);
        }
    }
}
