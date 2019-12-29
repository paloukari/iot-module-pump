using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.Devices.Client.HsmAuthentication;
using Microsoft.Azure.Devices.Client.HsmAuthentication.Transport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProxyKit;

namespace ReverseProxy
{
    public class Startup
    {
        const string ProxyTargetSocketUrlConfigKey = "ProxyTargetSocketUrl";

        private string _targetSocketUrl;

        public Startup(IConfiguration configuration)
        {
            _targetSocketUrl = configuration[ProxyTargetSocketUrlConfigKey] ?? throw new InvalidOperationException($"Environment variable {ProxyTargetSocketUrlConfigKey} is required.");
        }
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddProxy(
                httpClientBuilder => httpClientBuilder.ConfigurePrimaryHttpMessageHandler(
                    () => new HttpUdsMessageHandler(new Uri(_targetSocketUrl))));
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            var baseUrr = HttpClientHelper.GetBaseUrl(new Uri(_targetSocketUrl));
            app.RunProxy(context =>
            {
                var forwardContext = context.ForwardTo(baseUrr);
                return forwardContext.Send();
            });
        }
    }
}
