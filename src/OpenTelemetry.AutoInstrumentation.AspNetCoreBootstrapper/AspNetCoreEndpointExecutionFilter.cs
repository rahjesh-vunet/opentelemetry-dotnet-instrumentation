// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using Microsoft.AspNetCore.Mvc.Filters;

namespace OpenTelemetry.AutoInstrumentation.AspNetCoreBootstrapper;

/// <summary>
/// Wraps MVC/Web API controller action execution in its own child activity, nested inside the
/// <see cref="AspNetCorePipelineStartupFilter"/>'s pipeline span. This is the Core equivalent of
/// the "ExecuteRequestHandler" stage in the .NET Framework pipeline decomposition: the actual
/// application code that handles the request, as opposed to routing/authentication/authorization
/// framework machinery. Registered globally via <c>MvcOptions.Filters</c> so it applies to every
/// controller action without the target application needing any code changes.
/// </summary>
internal class AspNetCoreEndpointExecutionFilter : IAsyncActionFilter
{
    private static readonly ActivitySource Source =
        new("OpenTelemetry.AutoInstrumentation.AspNetCorePipeline");

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        Activity? activity = null;
        try
        {
            // Activity.Current here is naturally the "RequestPipeline" activity started by
            // AspNetCorePipelineStartupFilter's middleware -- Activity.Current flows through the
            // async call chain automatically, unlike System.Web's older event-handler model where
            // that wasn't reliable enough to depend on without explicitly capturing the context.
            if (Activity.Current != null)
            {
                activity = Source.StartActivity("ExecuteRequestHandler", ActivityKind.Internal, Activity.Current.Context);
                activity?.SetTag("vunet.code.level", true);
            }
        }
        catch (Exception)
        {
            // never break the real request
        }

        try
        {
            await next();
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
    }
}
