namespace MiIALocal.Core;

public static class VisualWorkflows
{
    public static Dictionary<string, object> Build(VisualRequest r, string prefix, string? reference = null)
    {
        r.Validate();
        if (prefix.Length != 32 || !prefix.All(Uri.IsHexDigit)) throw new InvalidDataException("Destino visual inválido.");
        if (reference is not null && (Path.GetFileName(reference) != reference || reference.IndexOfAny(['/', '\\', ':']) >= 0)) throw new InvalidDataException("Referencia visual inválida.");
        if (r.Task == VisualTask.Edit && reference is null) throw new InvalidDataException("Seleccioná una imagen para editar.");
        var graph = new Dictionary<string, object>();
        void Node(string id, string type, params (string, object)[] inputs) => graph[id] = new { class_type = type, inputs = inputs.ToDictionary(x => x.Item1, x => x.Item2) };
        object[] Link(string id, int slot = 0) => [id, slot];
        var animation = r.Task == VisualTask.Animation;
        Node("1", "UNETLoader", ("unet_name", animation ? VisualCatalog.Wan.Asset.FileName : VisualCatalog.Flux.Asset.FileName), ("weight_dtype", "default"));
        Node("2", "CLIPLoader", ("clip_name", animation ? VisualCatalog.WanText.Asset.FileName : VisualCatalog.FluxText.Asset.FileName), ("type", animation ? "wan" : "flux2"), ("device", "default"));
        Node("3", "VAELoader", ("vae_name", animation ? VisualCatalog.WanVae.Asset.FileName : VisualCatalog.FluxVae.Asset.FileName));
        Node("4", "CLIPTextEncode", ("clip", Link("2")), ("text", animation ? VisualAnimation.Prompt(r.Prompt, reference is not null) : r.Prompt));
        if (animation) Node("5", "CLIPTextEncode", ("clip", Link("2")), ("text", VisualAnimation.NegativePrompt));
        else Node("5", "ConditioningZeroOut", ("conditioning", Link("4")));
        if (reference is not null) Node("20", "LoadImage", ("image", reference));
        if (!animation)
        {
            Node("6", "EmptyFlux2LatentImage", ("width", r.Width), ("height", r.Height), ("batch_size", 1));
            var positive = Link("4"); var negative = Link("5");
            if (reference is not null)
            {
                Node("21", "ImageScale", ("image", Link("20")), ("upscale_method", "lanczos"), ("width", r.Width), ("height", r.Height), ("crop", "disabled"));
                Node("22", "VAEEncode", ("pixels", Link("21")), ("vae", Link("3")));
                Node("23", "ReferenceLatent", ("conditioning", positive), ("latent", Link("22")));
                Node("24", "ReferenceLatent", ("conditioning", negative), ("latent", Link("22")));
                positive = Link("23"); negative = Link("24");
            }
            Node("7", "RandomNoise", ("noise_seed", r.Seed));
            Node("8", "KSamplerSelect", ("sampler_name", "euler"));
            Node("9", "Flux2Scheduler", ("steps", 4), ("width", r.Width), ("height", r.Height));
            Node("10", "CFGGuider", ("model", Link("1")), ("positive", positive), ("negative", negative), ("cfg", 1.0));
            Node("11", "SamplerCustomAdvanced", ("noise", Link("7")), ("guider", Link("10")), ("sampler", Link("8")), ("sigmas", Link("9")), ("latent_image", Link("6")));
        }
        else
        {
            Node("6", "Wan22ImageToVideoLatent", ("vae", Link("3")), ("width", r.Width), ("height", r.Height), ("length", r.Frames), ("batch_size", 1));
            if (reference is not null)
            {
                Node("21", "ForjaFitReference", ("image", Link("20")), ("width", r.Width), ("height", r.Height));
                Node("6", "Wan22ImageToVideoLatent", ("vae", Link("3")), ("start_image", Link("21")), ("width", r.Width), ("height", r.Height), ("length", r.Frames), ("batch_size", 1));
            }
            Node("7", "ModelSamplingSD3", ("model", Link("1")), ("shift", 8.0));
            Node("11", "KSampler", ("model", Link("7")), ("positive", Link("4")), ("negative", Link("5")), ("latent_image", Link("6")), ("seed", r.Seed), ("steps", r.Steps), ("cfg", 5.0), ("sampler_name", "uni_pc"), ("scheduler", "simple"), ("denoise", 1.0));
        }
        Node("12", "VAEDecode", ("samples", Link("11")), ("vae", Link("3")));
        if (!animation) Node("13", "SaveImage", ("images", Link("12")), ("filename_prefix", prefix));
        else
        {
            Node("13", "CreateVideo", ("images", Link("12")), ("fps", 24.0));
            // ComfyUI's API accepts flattened DynamicCombo inputs; nesting silently drops format.
            Node("14", "SaveVideo", ("video", Link("13")), ("filename_prefix", prefix), ("format", "mp4"), ("format.codec", "h264"));
            Node("15", "ForjaSaveGif", ("images", Link("12")), ("filename_prefix", prefix), ("fps", 24));
        }
        return graph;
    }
}
