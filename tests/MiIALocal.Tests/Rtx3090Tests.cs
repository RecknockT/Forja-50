using MiIALocal.Core;
using System.Text.Json;

static class Rtx3090Tests
{
    public static async Task RunAsync(string root, Action<bool, string> assert, Func<string, Func<Task>, Task> test)
    {
        var gpu = new GpuInfo(2, "NVIDIA GeForce RTX 3090", 24576, 23000, "test");
        await test("3090: elegir sm86, 128K y mayor ventana GPU sin iniciar procesos", () =>
        {
            var settings = new AppSettings { LicenseAccepted = true, LastProject = "proyecto", ImportedNinferModel = "modelo-Todd.ninfer" };
            var preset = GpuProfiles.Apply(settings, gpu);
            assert(settings.EngineMode == EngineMode.KvMem && !settings.LicenseAccepted, "La 3090 conservó el backend incompatible o aceptó una licencia distinta.");
            assert(preset.Id == GpuProfiles.Rtx3090Id && !preset.BasicContextProbeValidated, "Perfil/validación física incorrectos.");
            assert(settings.ContextTokens == 131072 && settings.Tuning.HistoryTokens == 98304 && settings.Tuning.GenerationReserve == 16384, "No se aprovechó la memoria extra.");
            assert(settings.Tuning.EffectiveHostMiB(settings.ContextTokens) == 8192 && settings.Tuning.PrefillChunk == 512, "Caché o prefill incorrectos.");
            assert(settings.LastProject == "proyecto" && settings.ImportedNinferModel == "modelo-Todd.ninfer", "Se perdieron datos al cambiar el backend.");
            settings.Tuning.Validate(settings.ContextTokens);
            var assets = ReleaseProfile.ComponentsFor(settings);
            assert(assets.Contains(ReleaseProfile.Engine3090) && !assets.Contains(ReleaseProfile.Engine) && !assets.Contains(ReleaseProfile.NativeEngine), "Se descargará un motor RTX 50.");
            var paths = new AppPaths(Path.Combine(root, "rtx3090"));
            assert(paths.KvMemEngineExe(settings).Contains("rtx30-sm86") && paths.KvMemEngineExe(settings).Contains(ReleaseProfile.Engine3090.Id), "Ruta del motor incompatible.");
            var arguments = RuntimeSupervisor.EngineArguments(paths.ActiveModelPath(settings), gpu.Index, 1234, "key", settings.ContextTokens, settings.Tuning);
            string value(string flag) => arguments[Array.IndexOf(arguments, flag) + 1];
            assert(value("--device") == "2" && value("--max-context") == "131072" && value("--kvmem-budget") == "98304" && value("--kvmem-gen-reserve") == "16384" && value("--kvmem-host-mib") == "8192", "El motor no recibirá el perfil 3090.");
            using var config = JsonDocument.Parse(AgentConfiguration.Build(1234, "key", "instructions.md", settings.ContextTokens, settings.Tuning));
            assert(config.RootElement.GetProperty("provider").GetProperty("local").GetProperty("models").GetProperty("qwen-local").GetProperty("limit").GetProperty("context").GetInt32() == 131072, "El agente tiene otro contexto.");
            paths.SaveSettings(settings);
            assert(paths.LoadSettings().ContextTokens == 131072 && paths.LoadSettings().HardwareProfileId == preset.Id, "El perfil no sobrevive al reinicio.");
            assert(GpuProfiles.MinimumFreeMiB(settings, gpu) == 20000, "Se reutilizó el umbral de la 5080.");
            return Task.CompletedTask;
        });
        await test("3090: preservar personalizaciones y rechazar paquetes incompatibles", () =>
        {
            var settings = new AppSettings { EngineMode = EngineMode.KvMem, UseRecommendedProfile = false, ContextTokens = 65536, LicenseAccepted = true, Tuning = new() { MaxOutput = 4096, PrefillChunk = 256 } };
            GpuProfiles.Apply(settings, gpu);
            assert(settings.ContextTokens == 65536 && settings.Tuning.MaxOutput == 4096 && settings.LicenseAccepted, "Se pisó un ajuste manual KVMem.");
            foreach (var tokens in GpuProfiles.KvMemContexts(settings)) GpuProfiles.For(gpu).CreateTuning(tokens).Validate(tokens);
            try { Ninfer5080Profile.For(gpu); throw new Exception("Se admitió la 3090 en NInfer-5080."); } catch (InvalidDataException) { }
            settings.EngineMode = EngineMode.NInfer5080;
            try { ReleaseProfile.ComponentsFor(settings); throw new Exception("Se eligió un motor sm120a."); } catch (InvalidDataException) { }
            var paths = new AppPaths(Path.Combine(root, "rtx3090-ready"));
            using var http = new HttpClient(); var provisioner = new Provisioner(paths, new VerifiedDownloader(http));
            assert(!provisioner.IsPrepared(settings), "Se aceptó una preparación incompatible.");
            settings.EngineMode = EngineMode.KvMem;
            foreach (var file in new[] { paths.AgentExe, paths.GitExe, paths.NodeExe, paths.ActiveModelPath(settings), paths.EngineExe }) { Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, "fixture"); }
            assert(!provisioner.IsPrepared(settings), "El binario de RTX 50 se confundió con el de 3090.");
            Directory.CreateDirectory(Path.GetDirectoryName(paths.KvMemEngineExe(settings))!); File.WriteAllText(paths.KvMemEngineExe(settings), "fixture");
            assert(provisioner.IsPrepared(settings), "No se reconoció el paquete de la 3090.");
            return Task.CompletedTask;
        });
    }
}
