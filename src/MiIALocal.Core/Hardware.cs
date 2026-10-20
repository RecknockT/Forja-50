using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace MiIALocal.Core;

public sealed record GpuInfo(int Index, string Name, long TotalMiB, long FreeMiB, string Driver)
{
    public bool IsSupported => Name is "NVIDIA GeForce RTX 5080" or "NVIDIA GeForce RTX 5070 Ti";
    public string Profile => Name == "NVIDIA GeForce RTX 5080" ? "RTX 5080 · escritorio" : "RTX 5070 Ti · escritorio";
}
public sealed record HardwareReport(IReadOnlyList<GpuInfo> Gpus, double RamGiB)
{
    public GpuInfo? SelectedGpu => Gpus.FirstOrDefault(g => g.IsSupported && g.TotalMiB >= 15000);
    public string? Problem => SelectedGpu is null ? "Esta versión requiere una RTX 5070 Ti o RTX 5080 de escritorio con 16 GB. No admite variantes Laptop ni otras placas." : RamGiB < 30 ? "Este perfil requiere 32 GB de RAM instalados." : null;
}

public static class HardwareDetector
{
    public static IReadOnlyList<GpuInfo> ParseGpus(string output)
    {
        var gpus = new List<GpuInfo>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var c = line.Split(',', StringSplitOptions.TrimEntries);
            if (c.Length != 5 || !int.TryParse(c[0], out var i) || !long.TryParse(c[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var total) || !long.TryParse(c[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var free)) continue;
            gpus.Add(new(i, c[1], total, free, c[4]));
        }
        return gpus;
    }
    public static async Task<HardwareReport> DetectAsync(CancellationToken ct)
    {
        var psi = new ProcessStartInfo("nvidia-smi") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("--query-gpu=index,name,memory.total,memory.free,driver_version");
        psi.ArgumentList.Add("--format=csv,noheader,nounits");
        try
        {
            using var p = Process.Start(psi) ?? throw new IOException("No se pudo consultar la placa.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var stdout = p.StandardOutput.ReadToEndAsync(timeout.Token); var stderr = p.StandardError.ReadToEndAsync(timeout.Token);
            try { await p.WaitForExitAsync(timeout.Token); }
            catch { if (!p.HasExited) p.Kill(true); throw; }
            if (p.ExitCode != 0) throw new IOException("NVIDIA no pudo informar la placa. Revisá el driver. " + await stderr);
            var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            if (!GlobalMemoryStatusEx(ref memory)) throw new IOException("No se pudo consultar la memoria de Windows.");
            return new(ParseGpus(await stdout), memory.TotalPhysical / 1073741824d);
        }
        catch (System.ComponentModel.Win32Exception ex) { throw new IOException("No se encontró nvidia-smi. Instalá el controlador oficial de NVIDIA antes de preparar la IA.", ex); }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus { public uint Length, Load; public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, Extended; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
