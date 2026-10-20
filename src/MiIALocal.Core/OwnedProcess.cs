using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MiIALocal.Core;

public sealed class OwnedProcess : IAsyncDisposable
{
    private readonly Process process;
    private readonly SafeFileHandle job;
    private readonly Task stdout, stderr;
    public int Id => process.Id;
    public bool HasExited => process.HasExited;
    private OwnedProcess(Process process, SafeFileHandle job, Task stdout, Task stderr)
    { this.process = process; this.job = job; this.stdout = stdout; this.stderr = stderr; }

    public static OwnedProcess Start(string executable, IEnumerable<string> arguments, string workdir, string logBase, IReadOnlyDictionary<string, string>? environment = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(logBase)!);
        var info = new ProcessStartInfo(executable) { WorkingDirectory = workdir, CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        if (environment is not null) foreach (var (key, value) in environment) info.Environment[key] = value;
        var process = new Process { StartInfo = info };
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "No se pudo crear el supervisor de procesos.");
        try
        {
            var limits = new ExtendedLimit { BasicLimitInformation = new BasicLimit { LimitFlags = 0x2000 } }; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimit>())) throw new Win32Exception(Marshal.GetLastWin32Error());
            process.Start();
            _ = process.Handle; // Keep the real process handle through early termination.
            if (!AssignProcessToJobObject(job, process.Handle)) throw new Win32Exception(Marshal.GetLastWin32Error(), "No se pudo aislar el ciclo de vida del proceso.");
            return new(process, job, PumpAsync(process.StandardOutput, logBase + ".out.log"), PumpAsync(process.StandardError, logBase + ".err.log"));
        }
        catch { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } job.Dispose(); process.Dispose(); throw; }
    }
    private static async Task PumpAsync(StreamReader reader, string path)
    {
        await using var output = new StreamWriter(path, false, new System.Text.UTF8Encoding(false)) { AutoFlush = true };
        while (await reader.ReadLineAsync() is { } line) await output.WriteLineAsync(line);
    }
    public async Task WaitForReadyAsync(HttpClient client, Uri endpoint, string authorization, TimeSpan timeout, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(timeout);
        try
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (process.HasExited)
                {
                    await process.WaitForExitAsync(CancellationToken.None); await Task.WhenAll(stdout, stderr);
                    throw new IOException($"El proceso terminó durante el arranque (código {process.ExitCode}). Abrí Diagnóstico para ver la causa.");
                }
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                request.Headers.TryAddWithoutValidation("Authorization", authorization);
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token); attempt.CancelAfter(TimeSpan.FromSeconds(2));
                try { using var response = await client.SendAsync(request, attempt.Token); if (response.IsSuccessStatusCode) return; }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) { }
                await Task.Delay(300, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException("La IA tardó demasiado en responder. Revisá los registros antes de reintentar."); }
    }
    public async ValueTask DisposeAsync()
    {
        job.Dispose(); // Terminates only processes assigned to this job, including descendants.
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); } catch (TimeoutException) { if (!process.HasExited) process.Kill(true); }
        try { await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(5)); } catch (TimeoutException) { }
        process.Dispose();
    }
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimit { public long PerProcessUserTimeLimit, PerJobUserTimeLimit; public uint LimitFlags; public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass, SchedulingClass; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimit { public BasicLimit BasicLimitInformation; public IoCounters IoInfo; public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(SafeFileHandle job, int infoClass, ref ExtendedLimit info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
}
