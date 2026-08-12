// Copyright The OpenTelemetry Authors
// SPDX-License-Identifier: Apache-2.0

namespace OpenTelemetry.AutoInstrumentation.ClvCapture;

/// <summary>
/// vunet CLV proof of concept: builds the bytecode instrumentation definitions for the
/// methods whose arguments and return values should be captured.
///
/// The target is hard-coded here for now. This is the seam where a configuration loader
/// will plug in later — the definitions array is ordinary marshalled data, so it can be
/// produced from a file just as easily as from constants.
/// </summary>
internal static class ClvCaptureDefinitions
{
    private static readonly string IntegrationAssemblyName = typeof(ClvCaptureDefinitions).Assembly.FullName!;

    private static readonly string IntegrationTypeName = typeof(ClvCaptureIntegration).FullName!;

    /// <summary>
    /// Gets the definitions payload to hand to the native profiler.
    /// </summary>
    /// <returns>A payload with its own stable id, kept distinct from the built-in definitions.</returns>
    internal static InstrumentationDefinitions.Payload GetDefinitions()
    {
        return new InstrumentationDefinitions.Payload
        {
            // Distinct from the built-in payload ids so the native side keeps them separate
            // and does not reload these definitions once per AppDomain.
            DefinitionsId = "0C6F1E7B9A5D4C2E8F3B7A1D6E4C9B25",

            Definitions = new[]
            {
                // Banking.BLL.AccountService.GetBalance(Int32)
                //
                // Signature array is [returnType, arg1Type, ...]. "_" is a wildcard honoured
                // by the native matcher (rejit_preprocessor.cpp), so the concrete types do not
                // have to be spelled out. The ARGUMENT COUNT still has to be exact: the array
                // length minus one must equal the method's parameter count, which is why this
                // entry matches only the single-argument overload.
                //
                // The return type at index 0 is never compared by the matcher, so "_" is safe
                // there regardless of what GetBalance actually returns.
                new NativeCallTargetDefinition(
                    targetAssembly: "Banking.BLL",
                    targetType: "Banking.BLL.AccountService",
                    targetMethod: "GetBalance",
                    targetSignatureTypes: new[] { "_", "_" },
                    targetMinimumMajor: 0,
                    targetMinimumMinor: 0,
                    targetMinimumPatch: 0,
                    targetMaximumMajor: 65535,
                    targetMaximumMinor: 65535,
                    targetMaximumPatch: 65535,
                    integrationAssembly: IntegrationAssemblyName,
                    integrationType: IntegrationTypeName),
            },
        };
    }
}
