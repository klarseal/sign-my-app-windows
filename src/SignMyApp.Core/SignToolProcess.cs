using System.Diagnostics;

namespace SignMyApp.Core;

internal sealed record ToolResult(int ExitCode, string Output, string Error);
internal interface ISignToolProcess
{
    Task<ToolResult> RunAsync(string tool, IReadOnlyList<string> arguments, CancellationToken ct);
}

internal sealed class SignToolProcess : ISignToolProcess
{
    public async Task<ToolResult> RunAsync(string tool, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(tool)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(tool)!
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("SignTool could not be started.");
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var error = process.StandardError.ReadToEndAsync(ct);
        try
        {
            await process.WaitForExitAsync(ct);
            return new(process.ExitCode, await output, await error);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* It exited between the check and Kill. */ }
            await process.WaitForExitAsync(CancellationToken.None);
            try { await Task.WhenAll(output, error); } catch (OperationCanceledException) { }
            throw;
        }
    }
}
