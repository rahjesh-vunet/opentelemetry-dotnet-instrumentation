// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

#if NETFRAMEWORK
using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Web;
using OpenTelemetry.Trace;

namespace OpenTelemetry.AutoInstrumentation.Instrumentations.AspNet;

/// <summary>
/// Http module that decomposes the System.Web integrated pipeline into child activities,
/// to provide code-level visibility similar to Dynatrace OneAgent's "Code level" view.
/// </summary>
internal class AspNetPipelineHttpModule : IHttpModule
{
    private const string CurrentStageKey = "otel_aspnet_pipeline_current_stage";
    private const string RootContextKey = "otel_aspnet_pipeline_root_context";

    private static readonly ActivitySource Source =
        new("OpenTelemetry.AutoInstrumentation.AspNetPipeline");

    // Static constructors are guaranteed by the runtime to run exactly once per
    // AppDomain, before the type is first used -- even though Init(HttpApplication)
    // itself can be called many times across pooled HttpApplication instances.
    static AspNetPipelineHttpModule()
    {
        AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
    }

    /// <summary>
    /// Initializes a module and prepares it to handle requests.
    /// </summary>
    /// <param name="context">An <see cref="HttpApplication"/> that provides access to the methods, properties, and events common to all application objects within an ASP.NET application.</param>
    public void Init(HttpApplication context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.BeginRequest += (s, e) => OnBeginRequest(GetContext(s));
        context.AuthenticateRequest += (s, e) => Transition(GetContext(s), "AuthenticateRequest");
        context.PostAuthenticateRequest += (s, e) => Transition(GetContext(s), null);
        context.AuthorizeRequest += (s, e) => Transition(GetContext(s), "AuthorizeRequest");
        context.PostAuthorizeRequest += (s, e) => Transition(GetContext(s), null);
        context.ResolveRequestCache += (s, e) => Transition(GetContext(s), "ResolveRequestCache");
        context.PostResolveRequestCache += (s, e) => Transition(GetContext(s), "MapRequestHandler");
        context.PostMapRequestHandler += (s, e) => Transition(GetContext(s), null);
        context.AcquireRequestState += (s, e) => Transition(GetContext(s), "AcquireRequestState");
        context.PostAcquireRequestState += (s, e) => Transition(GetContext(s), null);
        context.PreRequestHandlerExecute += (s, e) => Transition(GetContext(s), "ExecuteRequestHandler");
        context.PostRequestHandlerExecute += (s, e) => Transition(GetContext(s), null);
        context.ReleaseRequestState += (s, e) => Transition(GetContext(s), "ReleaseRequestState");
        context.PostReleaseRequestState += (s, e) => Transition(GetContext(s), null);
        context.UpdateRequestCache += (s, e) => Transition(GetContext(s), "UpdateRequestCache");
        context.PostUpdateRequestCache += (s, e) => Transition(GetContext(s), null);
        context.LogRequest += (s, e) => Transition(GetContext(s), "LogRequest");
        context.PostLogRequest += (s, e) => Transition(GetContext(s), null);
        context.EndRequest += (s, e) => Transition(GetContext(s), "EndRequest", isFinal: true);
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private static HttpContext GetContext(object sender) => ((HttpApplication)sender).Context;

    /// <summary>
    /// Handles AppDomain.FirstChanceException, which fires the instant any exception is
    /// thrown -- before any catch block runs -- regardless of whether it's later handled
    /// or propagates unhandled. This is what lets us capture both handled exceptions (caught
    /// and swallowed deep in application code, e.g. inside a controller's own try/catch) and
    /// unhandled ones (that become a 500), matching what DT's Code Level view shows: every
    /// exception raised during the request, not just the one that ends up in the response.
    /// </summary>
    private static void OnFirstChanceException(object sender, FirstChanceExceptionEventArgs e)
    {
        try
        {
            // Only record while a request is actively being traced; avoids attaching
            // exceptions to unrelated background/startup activity with no current span,
            // and avoids doing any work at all outside of a traced request.
            Activity.Current?.RecordException(e.Exception);
        }
        catch (Exception)
        {
            // never break the real request, and never let exception *capture* itself throw
        }
    }

    private static void OnBeginRequest(HttpContext context)
    {
        try
        {
            // Capture the root request activity's context now, while Activity.Current is still
            // reliably set. By the time EndRequest fires, TelemetryHttpModule's own EndRequest
            // handler (registered before ours) has already stopped the root activity, which
            // clears Activity.Current back to null since the root has no parent of its own.
            // If we relied on ambient Activity.Current for parenting at that point, our final
            // "EndRequest" span would start with no parent and become an orphaned root span in
            // a brand-new trace instead of a child of this request's trace. Capturing the
            // context explicitly here and passing it to every StartActivity call below avoids
            // that, regardless of module registration order or teardown timing.
            if (Activity.Current != null)
            {
                context.Items[RootContextKey] = Activity.Current.Context;
            }
        }
        catch (Exception)
        {
            // never break the real request
        }

        Transition(context, "BeginRequest");
    }

    private static void Transition(HttpContext context, string? nextStageName, bool isFinal = false)
    {
        try
        {
            if (context.Items[CurrentStageKey] is Activity current)
            {
                current.Stop();
                context.Items.Remove(CurrentStageKey);
            }

            if (nextStageName == null)
            {
                return;
            }

            if (!(context.Items[RootContextKey] is ActivityContext parentContext))
            {
                // Root context was never captured (Activity.Current was not yet set when
                // OnBeginRequest ran). Without a real parent, StartActivity would mint a
                // brand-new, unparented root trace instead of a child span. Skip creating
                // a pipeline-stage activity entirely rather than pollute the trace view
                // with an orphaned single-span trace.
                return;
            }

            var activity = Source.StartActivity(nextStageName, ActivityKind.Internal, parentContext);

            // Marks this as one of our synthetic pipeline-stage spans (BeginRequest,
            // ExecuteRequestHandler, etc.), mirroring the vunet.code.level=true tag used in
            // the Java CLV agent. Lets the APM UI filter/highlight code-level spans without
            // guessing based on span name alone.
            activity?.SetTag("vunet.code.level", true);

            if (isFinal)
            {
                activity?.Stop();
                return;
            }

            if (activity != null)
            {
                context.Items[CurrentStageKey] = activity;
            }
        }
        catch (Exception)
        {
            // never break the real request
        }
    }
}
#endif
