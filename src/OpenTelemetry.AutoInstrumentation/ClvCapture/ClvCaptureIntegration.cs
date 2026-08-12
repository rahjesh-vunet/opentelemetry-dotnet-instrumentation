// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using OpenTelemetry.AutoInstrumentation.CallTarget;
using OpenTelemetry.AutoInstrumentation.Logging;

namespace OpenTelemetry.AutoInstrumentation.ClvCapture;

/// <summary>
/// vunet CLV proof of concept: captures the argument and return value of a hard-coded
/// target method and writes them to the console.
///
/// Handles methods taking exactly ONE argument. The CallTarget fast path (FASTPATH_COUNT
/// in tracer_tokens.h) passes arguments as typed generics and IntegrationMapper requires
/// the OnMethodBegin arity to match the target, so one class per argument count is needed.
/// Arity 1 is enough for Banking.BLL.AccountService.GetBalance(Int32).
/// </summary>
public static class ClvCaptureIntegration
{
    private const int MaxValueLength = 200;

    private static readonly IOtelLogger Logger = OtelLogging.GetLogger();

    internal static CallTargetState OnMethodBegin<TTarget, TArg1>(TTarget instance, ref TArg1 arg1)
    {
        try
        {
            // Formatted now and carried to OnMethodEnd so the argument and the return value
            // can be reported together on one line.
            return new CallTargetState(activity: null, state: Format(arg1));
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "CLV capture: OnMethodBegin failed.");
            return CallTargetState.GetDefault();
        }
    }

    internal static CallTargetReturn<TReturn> OnMethodEnd<TTarget, TReturn>(TTarget instance, TReturn returnValue, Exception? exception, in CallTargetState state)
    {
        Report<TTarget, TReturn>(returnValue, exception, state.State as string);
        return new CallTargetReturn<TReturn>(returnValue);
    }

    internal static TReturn OnAsyncMethodEnd<TTarget, TReturn>(TTarget instance, TReturn returnValue, Exception? exception, CallTargetState state)
    {
        Report<TTarget, TReturn>(returnValue, exception, state.State as string);
        return returnValue;
    }

    private static void Report<TTarget, TReturn>(TReturn returnValue, Exception? exception, string? capturedArgument)
    {
        try
        {
            var builder = new StringBuilder("[CLV] ")
                .Append(typeof(TTarget).FullName)
                .Append(" | arg[0]=")
                .Append(capturedArgument ?? "<not captured>");

            if (exception != null)
            {
                builder.Append(" | threw=")
                       .Append(exception.GetType().FullName)
                       .Append(": ")
                       .Append(Truncate(exception.Message));
            }
            else
            {
                builder.Append(" | returned=").Append(Format(returnValue));
            }

            var line = builder.ToString();

            // Console for the standalone test application, and the agent log so the same
            // build is usable under IIS where no console is attached. The line is passed as
            // an argument rather than as the template, so braces in captured values are safe.
            Console.WriteLine(line);
            Logger.Information("{0}", line);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "CLV capture: reporting failed.");
        }
    }

    private static string Format<T>(T value)
    {
        if (value is null)
        {
            return "<null>";
        }

        try
        {
            // ToString only, for the proof of concept. Structured serialisation, depth and
            // size caps and a field allow-list belong here before this sees real payloads.
            return Truncate(value.ToString());
        }
        catch (Exception ex)
        {
            return "<threw " + ex.GetType().Name + ">";
        }
    }

    private static string Truncate(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value!.Length <= MaxValueLength
            ? value
            : value.Substring(0, MaxValueLength) + "...";
    }
}
