namespace MiIALocal.Core;

public sealed class EngineTuning
{
    public int HistoryTokens { get; set; } = 24576;
    public int GenerationReserve { get; set; } = 8192;
    public int? HostCacheMiB { get; set; }
    public int SinkTokens { get; set; } = 12288;
    public int RecentTokens { get; set; } = 1024;
    public string KvType { get; set; } = "rk8v4";
    public int PrefillChunk { get; set; } = 256;
    public int DraftTokens { get; set; } = 3;
    public int NgramDraftTokens { get; set; } = 15;
    public int NgramMinMatch { get; set; } = 12;
    public int ThinkingTokens { get; set; } = 2048;
    public int MaxOutput { get; set; } = 8192;
    public double Temperature { get; set; } = 1;
    public double TopP { get; set; } = .95;
    public int TopK { get; set; } = 20;
    public double MinP { get; set; }
    public double AgentTemperature { get; set; } = .6;
    public static readonly string[] KvTypes = ["rk8v4", "rk4v4", "rk4v4-e8", "rk2v4-e8", "k8v4", "nvfp4", "int8", "fp8", "bf16"];
    public int EffectiveHostMiB(int context) => HostCacheMiB ?? ContextProfile.For(context).HostCacheMiB;
    public void Validate(int context)
    {
        _ = ContextProfile.For(context);
        void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
        Require(HistoryTokens >= 1024 && HistoryTokens % 64 == 0, "El historial GPU debe ser al menos 1.024 y múltiplo de 64.");
        Require(GenerationReserve >= 1024 && GenerationReserve % 64 == 0, "La reserva debe ser al menos 1.024 y múltiplo de 64.");
        Require((long)HistoryTokens + GenerationReserve <= context, "Historial GPU + reserva no pueden superar el contexto.");
        Require(SinkTokens >= 0 && SinkTokens % 64 == 0 && RecentTokens >= 64 && RecentTokens % 64 == 0 && (long)SinkTokens + RecentTokens <= HistoryTokens, "Inicio y reciente deben ser múltiplos de 64 y caber en el historial; reciente mínimo 64.");
        Require(EffectiveHostMiB(context) is >= 512 and <= 8192, "La caché RAM debe estar entre 512 y 8.192 MiB.");
        Require(KvTypes.Contains(KvType), "Formato KV no admitido.");
        Require(PrefillChunk >= 128 && PrefillChunk <= 2048 && PrefillChunk % 128 == 0 && PrefillChunk <= GenerationReserve, "Prefill debe ser múltiplo de 128, entre 128 y 2.048, y caber en la reserva.");
        Require(DraftTokens is >= 1 and <= 4, "Los borradores MTP deben estar entre 1 y 4.");
        Require(NgramDraftTokens is >= 0 and <= 63 && NgramMinMatch is >= 4 and <= 64, "Ngram: borradores de 0 a 63 y coincidencia mínima de 4 a 64.");
        Require(MaxOutput is >= 128 and <= 16384 && MaxOutput < context, "La salida debe estar entre 128 y 16.384 tokens y ser menor que el contexto.");
        Require(ThinkingTokens >= 0 && ThinkingTokens <= MaxOutput, "El razonamiento debe estar entre 0 y el límite de salida.");
        Require(double.IsFinite(Temperature) && Temperature is >= 0 and <= 2, "Temperatura del motor: entre 0 y 2.");
        Require(double.IsFinite(AgentTemperature) && AgentTemperature is >= 0 and <= 2, "Temperatura del agente: entre 0 y 2.");
        Require(double.IsFinite(TopP) && TopP is > 0 and <= 1 && double.IsFinite(MinP) && MinP is >= 0 and <= 1 && TopK is >= 0 and <= 20, "Top P debe ser mayor que 0 y hasta 1; Min P entre 0 y 1; Top K entre 0 y 20.");
    }
}
