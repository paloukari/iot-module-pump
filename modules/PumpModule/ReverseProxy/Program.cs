using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ReverseProxy
{
    public class Program
    {
        const string ProxySourceUrlConfigKey = "ProxySourceUrl";
        
        public static void Main(string[] args)
        {
            var config = new ConfigurationBuilder()
               .SetBasePath(Directory.GetCurrentDirectory())
               .AddEnvironmentVariables()
               .AddCommandLine(args)
               .Build();

            var proxySourceUrl = config[ProxySourceUrlConfigKey] ?? throw new InvalidOperationException($"Environment variable {ProxySourceUrlConfigKey} is required.");

            var host = WebHost.CreateDefaultBuilder(args)
                .UseConfiguration(config)
                .UseStartup<Startup>()
                .UseUrls(proxySourceUrl)
                .Build();

            host.Run();
        }
    }
}
