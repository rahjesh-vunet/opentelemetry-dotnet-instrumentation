// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.AutoInstrumentation.Configurations;
using OpenTelemetry.AutoInstrumentation.Logger;
using OpenTelemetry.AutoInstrumentation.Logging;

[assembly: HostingStartup(typeof(OpenTelemetry.AutoInstrumentation.AspNetCoreBootstrapper.BootstrapperHostingStartup))]

namespace OpenTelemetry.AutoInstrumentation.AspNetCoreBootstrapper;

/// <summary>
/// Add summary.
/// </summary>
internal class BootstrapperHostingStartup : IHostingStartup
{
    private static readonly IOtelLogger Logger = OtelLogging.GetLogger("AspNetCoreBootstrapper");

    private readonly LogSettings _logSettings;

    /// <summary>
    /// Initializes a new instance of the <see cref="BootstrapperHostingStartup"/> class.
    /// </summary>
    public BootstrapperHostingStartup()
    {
        _logSettings = Instrumentation.LogSettings.Value;
    }

    /// <summary>
    /// This method gets called by the runtime to lightup ASP.NET Core OpenTelemetry Logs Collection.
    /// </summary>
    /// <param name="builder">The <see cref="IWebHostBuilder"/>.</param>
    public void Configure(IWebHostBuilder builder)
    {
        ConfigurePipelineCodeLevelVisibility(builder);

        if (!_logSettings.LogsEnabled)
        {
            Logger.Information("BootstrapperHostingStartup loaded, but OpenTelemetry Logs disabled. Skipping.");
            return;
        }

        if (!_logSettings.EnabledInstrumentations.Contains(LogInstrumentation.ILogger))
        {
            Logger.Information($"BootstrapperHostingStartup loaded, but {nameof(LogInstrumentation.ILogger)} instrumentation is disabled. Skipping.");
            return;
        }

        try
        {
            builder.ConfigureLogging(logging => logging.AddOpenTelemetryLogsFromStartup());

            var applicationName = GetApplicationName();
            Logger.Information($"BootstrapperHostingStartup loaded for application with name {applicationName}.");
        }
        catch (Exception ex)
        {
            Logger.Error($"Error in BootstrapperHostingStartup: {ex}");
            throw;
        }
    }

    /// <summary>
    /// Registers the pipeline-wrapping middleware and the MVC action filter that together
    /// provide code-level visibility for ASP.NET Core apps, mirroring
    /// AspNetPipelineHttpModule's role for .NET Framework apps. Runs unconditionally when this
    /// HostingStartup loads (i.e. whenever automatic instrumentation itself is active), matching
    /// how the Framework pipeline module is wired in via HttpModuleIntegration.
    /// </summary>
    private static void ConfigurePipelineCodeLevelVisibility(IWebHostBuilder builder)
    {
        try
        {
            builder.ConfigureServices(services =>
            {
                services.AddTransient<Microsoft.AspNetCore.Hosting.IStartupFilter, AspNetCorePipelineStartupFilter>();

                // Inert if the application never calls AddControllers()/AddMvc(): this just
                // registers a configuration delegate for MvcOptions, it does not require MVC's
                // own services to be present.
                services.Configure<MvcOptions>(options => options.Filters.Add<AspNetCoreEndpointExecutionFilter>());
            });
        }
        catch (Exception ex)
        {
            Logger.Error($"Error configuring pipeline code-level visibility in BootstrapperHostingStartup: {ex}");
        }
    }

    private static string GetApplicationName()
    {
        try
        {
            return AppDomain.CurrentDomain.FriendlyName;
        }
        catch (Exception ex)
        {
            Logger.Error($"Error getting AppDomain.CurrentDomain.FriendlyName: {ex}");
            return string.Empty;
        }
    }
}
