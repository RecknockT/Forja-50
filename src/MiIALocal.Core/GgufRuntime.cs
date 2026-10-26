namespace MiIALocal.Core;

/// <summary>Pinned GGUF loader and isolated Python dependency, only prepared for RTX 3090.</summary>
public sealed class GgufRuntime(AppPaths paths, VerifiedDownloader downloader)
{
    public const string Revision = "6ea2651e7df66d7585f6ffee804b20e92fb38b8a";
    public static readonly DownloadAsset Loader = new("visual-gguf-loader", "Cargador GGUF para RTX 3090",
        "https://codeload.github.com/city96/ComfyUI-GGUF/zip/" + Revision,
        "aad273a0b774684285b4496f6edff7e2293bd29d71ef89926d6861bfdf4947ac", "ComfyUI-GGUF-" + Revision + ".zip", 38051);
    public static readonly DownloadAsset Dependency = new("visual-gguf-python", "Biblioteca GGUF 0.17.1",
        "https://files.pythonhosted.org/packages/fc/31/6a93a887617ee7deeaa602ca3d02d1c12a6cb8a742a695de5d128f5fa46a/gguf-0.17.1-py3-none-any.whl",
        "7bc5aa7eeb1931f7d39b48fdc5b38fda6b294b9dca75cf607ac69557840a3943", "gguf-0.17.1-py3-none-any.whl", 96224);
    public static DownloadAsset[] Assets => [Loader, Dependency];
    private string Target => Path.Combine(paths.ComfyRoot, "ComfyUI", "custom_nodes", "ComfyUI-GGUF");
    private string MarkerName => ".forja-" + Loader.Sha256[..16] + "-" + Dependency.Sha256[..16];
    public bool IsPrepared => File.Exists(Path.Combine(Target, MarkerName))
        && File.Exists(Path.Combine(Target, "__init__.py")) && File.Exists(Path.Combine(Target, "nodes.py"))
        && File.Exists(Path.Combine(Target, "_forja_deps", "gguf", "__init__.py"));

    public async Task PrepareAsync(IProgress<PreparationProgress>? progress, CancellationToken ct)
    {
        if (IsPrepared) return;
        foreach (var asset in Assets)
            await downloader.DownloadAsync(asset, Path.Combine(paths.Downloads, asset.FileName), progress, ct);
        var staging = Target + ".stage-" + Guid.NewGuid().ToString("N");
        try
        {
            // Extract only authenticated archives. Keep gguf private to this loader;
            // never pip-install into or replace the runtime's shared packages.
            SafeArchive.Extract(Path.Combine(paths.Downloads, Loader.FileName), staging, ct);
            var inner = Path.Combine(staging, "ComfyUI-GGUF-" + Revision);
            SafeArchive.Extract(Path.Combine(paths.Downloads, Dependency.FileName), Path.Combine(inner, "_forja_deps"), ct);
            if (!File.Exists(Path.Combine(inner, "nodes.py")) || !File.Exists(Path.Combine(inner, "_forja_deps", "gguf", "__init__.py")))
                throw new InvalidDataException("El paquete GGUF no tiene la estructura esperada.");
            var entry = Path.Combine(inner, "__init__.py");
            AppPaths.AtomicWrite(entry, "import sys as _sys\nfrom pathlib import Path as _Path\n_sys.path.insert(0, str(_Path(__file__).parent / '_forja_deps'))\n" + File.ReadAllText(entry));
            AppPaths.AtomicWrite(Path.Combine(inner, MarkerName), Loader.Sha256 + "\n" + Dependency.Sha256);
            ct.ThrowIfCancellationRequested();
            if (Directory.Exists(Target)) Directory.Move(Target, Target + ".previous-" + Guid.NewGuid().ToString("N"));
            Directory.Move(inner, Target);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }
}
