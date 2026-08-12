// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using OpenTelemetry.Trace;

namespace OpenTelemetry.AutoInstrumentation.AspNetCoreBootstrapper;

/// <summary>
/// Wraps the whole ASP.NET Core middleware pipeline (routing, authentication, authorization,
/// and endpoint execution) in a child activity, to provide code-level visibility similar to
/// Dynatrace OneAgent's "Code level" view. Unlike System.Web's named lifecycle events
/// (BeginRequest, AuthenticateRequest, etc.), ASP.NET Core's pipeline is a flexible chain of
/// user-configured middleware with no fixed named stages, so this wraps the pipeline as a
/// whole rather than decomposing it into the same granular stage list used for .NET Framework.
/// See <see cref="AspNetCoreEndpointExecutionFilter"/> for the finer-grained span that isolates
/// the actual controller/handler code specifically, nested inside this one.
/// </summary>
internal class AspNetCorePipelineStartupFilter : IStartupFilter
{
    private const string RootContextKey = "otel_aspnetcore_pipeline_root_context";

    private static readonly ActivitySource Source =
        new("OpenTelemetry.AutoInstrumentation.AspNetCorePipeline");

    // Same static-hook pattern as AspNetPipelineHttpModule's Framework equivalent:
    // AppDomain.FirstChanceException is a CLR-level hook, identical on Framework and Core,
    // that fires the instant any exception is thrown -- before any catch block runs -- so it
    // captures both handled exceptions (caught and swallowed deep in application code) and
    // unhandled ones. A static constructor runs exactly once per AppDomain/process, so this
    // is safe even though Configure(...) below can run once per host builder.
    static AspNetCorePipelineStartupFilter()
    {
        AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
    }

    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(async (context, requestDelegateNext) =>
            {
                Activity? activity = null;
                try
                {
                    // Capture the root request activity's context now, while Activity.Current is
                    // still reliably the root span created by OpenTelemetry.Instrumentation.AspNetCore.
                    // Explicitly stashing it (rather than relying on ambient Activity.Current when
                    // this middleware's "after next" code runs) protects against any middleware later
                    // in the chain that changes Activity.Current, or clears it before returning.
                    if (Activity.Current != null)
                    {
                        context.Items[RootContextKey] = Activity.Current.Context;
                        activity = Source.StartActivity("RequestPipeline", ActivityKind.Internal, Activity.Current.Context);

                        // Marks this as one of our synthetic pipeline spans, mirroring the
                        // vunet.code.level=true tag used in the Java CLV agent. Lets the APM UI
                        // filter/highlight code-level spans without guessing based on span name alone.
                        activity?.SetTag("vunet.code.level", true);
                    }
                }
                catch (Exception)
                {
                    // never break the real request
                }

                try
                {
                    await requestDelegateNext();
                }
                finally
                {
                    try
                    {
                        activity?.Stop();
                    }
                    catch (Exception)
                    {
                        // never break the real request
                    }
                }
            });

            next(app);
        };
    }

    private static void OnFirstChanceException(object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
    {
        try
        {
            // Only record while a request is actively being traced; avoids attaching
            // exceptions to unrelated background/startup activity with no current span.
            Activity.Current?.RecordException(e.Exception);
        }
        catch (Exception)
        {
            // never break the real request, and never let exception *capture* itself throw
        }
    }
}
