namespace MiIALocal.Core;

public static class VisualWorkflows
{
    public static Dictionary<string, object> Build(VisualRequest r, string prefix, string? reference = null, string? preparedPrompt = null, string? referenceTitle = null)
    {
        Validate(r, prefix, reference);
        if (preparedPrompt is not null && (string.IsNullOrWhiteSpace(preparedPrompt) || preparedPrompt.Length > 12000))
            throw new InvalidDataException("La descripción revisada debe contener entre 1 y 12000 caracteres.");
        var graph = new Dictionary<string, object>();
        Dictionary<string, object> Node(string id, string type, params (string, object)[] inputs)
        { var values = inputs.ToDictionary(x => x.Item1, x => x.Item2); graph[id] = new { class_type = type, inputs = values }; return values; }
        object[] Link(string id, int slot = 0) => [id, slot];
        var animation = r.Task == VisualTask.Animation;
        var fullEdit = !animation && r.Task == VisualTask.Edit && r.FullEdit;
        if (reference is not null) Node("20", "LoadImage", ("image", reference));
        if (!animation)
        {
            Node("1", "CheckpointLoaderSimple", ("ckpt_name", (fullEdit ? VisualCatalog.HiDreamFull : r.HighPrecision ? VisualCatalog.HiDreamBf16 : VisualCatalog.HiDream).Asset.FileName));
            if (reference is not null)
                Node("21", "ForjaFitReference", ("image", Link("20")), ("width", r.Width), ("height", r.Height));
            if (preparedPrompt is not null)
                // The person already reviewed the assistant text: the diffusion stage receives it verbatim.
                Node("4", "CLIPTextEncode", ("clip", Link("1", 1)), ("text", preparedPrompt));
            else
            {
                var instruction = Instruction(r, reference is not null);
                object prepared = instruction;
                if (r.EnhancePrompt)
                {
                    AddAssistant(graph, AssistantInput(r, referenceTitle), SystemPrompt(r, reference is not null), reference is not null, r.Seed);
                    prepared = Link("31");
                }
                Node("32", "ForjaPreparedPrompt", ("text", prepared));
                Node("4", "CLIPTextEncode", ("clip", Link("1", 1)), ("text", Link("32")));
            }
            // The Full checkpoint is not distilled: it needs a real negative branch for CFG 5 and the
            // recipe of the official Full template (40 steps, dpmpp_2m_sde_gpu, seam smoothing).
            if (fullEdit) Node("5", "CLIPTextEncode", ("clip", Link("1", 1)), ("text", ""));
            else Node("5", "ConditioningZeroOut", ("conditioning", Link("4")));
            Node("6", "EmptyHiDreamO1LatentImage", ("width", r.Width), ("height", r.Height), ("batch_size", 1));
            var positive = Link("4"); var negative = Link("5");
            if (reference is not null)
            {
                Node("23", "HiDreamO1ReferenceImages", ("positive", positive), ("negative", negative), ("images.image_1", Link("21")));
                positive = Link("23"); negative = Link("23", 1);
            }
            Node("7", "ModelNoiseScale", ("model", Link("1")), ("noise_scale", fullEdit ? 8.0 : 7.6));
            var model = Link("7");
            if (fullEdit)
            {
                Node("10", "HiDreamO1PatchSeamSmoothing", ("model", Link("7")), ("start_percent", 0.8), ("end_percent", 1.0),
                    ("pattern", "single_shift"), ("passes", "ramp_2_4"), ("blend", "median"), ("strength", 1.0));
                model = Link("10");
            }
            Node("8", "BasicScheduler", ("model", model), ("scheduler", "normal"), ("steps", fullEdit ? 40 : 28), ("denoise", 1.0));
            // Dev editing follows HiDream's flow-match scheduler: plain Euler, without the per-step noise
            // re-injection of the distilled creation path, which produced noise on an edit.
            if (fullEdit) Node("9", "KSamplerSelect", ("sampler_name", "dpmpp_2m_sde_gpu"));
            else if (reference is not null) Node("9", "KSamplerSelect", ("sampler_name", "euler"));
            else Node("9", "SamplerLCM", ("s_noise", 1.0), ("s_noise_end", 1.0), ("noise_clip_std", 2.5));
            Node("11", "SamplerCustom", ("model", model), ("positive", positive), ("negative", negative), ("latent_image", Link("6")),
                ("add_noise", true), ("noise_seed", r.Seed), ("cfg", fullEdit ? 5.0 : 1.0), ("sampler", Link("9")), ("sigmas", Link("8")));
        }
        else
        {
            Node("1", "UNETLoader", ("unet_name", VisualCatalog.Wan.Asset.FileName), ("weight_dtype", "default"));
            Node("2", "CLIPLoader", ("clip_name", VisualCatalog.WanText.Asset.FileName), ("type", "wan"), ("device", "default"));
            Node("3", "VAELoader", ("vae_name", VisualCatalog.WanVae.Asset.FileName));
            // The assistant, when active, already produced the English motion instruction.
            Node("4", "CLIPTextEncode", ("clip", Link("2")), ("text", VisualAnimation.Prompt(preparedPrompt ?? r.Prompt, reference is not null)));
            Node("5", "CLIPTextEncode", ("clip", Link("2")), ("text", VisualAnimation.NegativePrompt));
            Node("6", "Wan22ImageToVideoLatent", ("vae", Link("3")), ("width", r.Width), ("height", r.Height), ("length", r.Frames), ("batch_size", 1));
            if (reference is not null)
            {
                Node("21", "ForjaFitReference", ("image", Link("20")), ("width", r.Width), ("height", r.Height));
                Node("6", "Wan22ImageToVideoLatent", ("vae", Link("3")), ("start_image", Link("21")), ("width", r.Width), ("height", r.Height), ("length", r.Frames), ("batch_size", 1));
            }
            Node("7", "ModelSamplingSD3", ("model", Link("1")), ("shift", 8.0));
            Node("11", "KSampler", ("model", Link("7")), ("positive", Link("4")), ("negative", Link("5")), ("latent_image", Link("6")), ("seed", r.Seed), ("steps", r.Steps), ("cfg", 5.0), ("sampler_name", "uni_pc"), ("scheduler", "simple"), ("denoise", 1.0));
        }
        Node("12", "VAEDecode", ("samples", Link("11")), ("vae", animation ? Link("3") : Link("1", 2)));
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

    /// <summary>Assistant-only graph: the description is ready before any diffusion component loads.</summary>
    public static Dictionary<string, object> BuildPrepare(VisualRequest r, string? reference, string? referenceTitle = null)
    {
        Validate(r, null, reference);
        if (!r.EnhancePrompt) throw new InvalidDataException("El asistente de descripción está desactivado.");
        var graph = new Dictionary<string, object>();
        void Node(string id, string type, params (string, object)[] inputs)
        { var values = inputs.ToDictionary(x => x.Item1, x => x.Item2); graph[id] = new { class_type = type, inputs = values }; }
        object[] Link(string id, int slot = 0) => [id, slot];
        if (reference is not null)
        {
            Node("20", "LoadImage", ("image", reference));
            Node("21", "ForjaFitReference", ("image", Link("20")), ("width", r.Width), ("height", r.Height));
        }
        AddAssistant(graph, AssistantInput(r, referenceTitle), SystemPrompt(r, reference is not null), reference is not null, r.Seed);
        Node("32", "ForjaPreparedPrompt", ("text", Link("31")));
        return graph;
    }

    private static void AddAssistant(Dictionary<string, object> graph, string instruction, string systemPrompt, bool hasReference, long seed)
    {
        void Node(string id, string type, params (string, object)[] inputs)
        { var values = inputs.ToDictionary(x => x.Item1, x => x.Item2); graph[id] = new { class_type = type, inputs = values }; }
        object[] Link(string id, int slot = 0) => [id, slot];
        Node("30", "ForjaAssistantLoader", ("clip_name", VisualCatalog.PromptAssistant.Asset.FileName));
        var preparation = AddTextGenerate(graph, instruction, systemPrompt, seed);
        if (hasReference)
        {
            // The assistant only needs a bounded preview; keep the detailed
            // reference in node 21 for HiDream without doubling vision memory.
            Node("22", "ForjaFitReference", ("image", Link("20")), ("width", 1024), ("height", 1024));
            preparation["image"] = Link("22");
        }
    }

    private static Dictionary<string, object> AddTextGenerate(Dictionary<string, object> graph, string instruction, string systemPrompt, long seed)
    {
        object[] Link(string id, int slot = 0) => [id, slot];
        var preparation = new Dictionary<string, object>
        {
            ["clip"] = Link("30"), ["prompt"] = instruction, ["system_prompt"] = systemPrompt,
            ["max_length"] = 512, ["sampling_mode"] = "on", ["sampling_mode.temperature"] = 0.5, ["sampling_mode.top_k"] = 64, ["sampling_mode.top_p"] = 0.9,
            ["sampling_mode.min_p"] = 0.02, ["sampling_mode.repetition_penalty"] = 1.05, ["sampling_mode.seed"] = seed, ["sampling_mode.presence_penalty"] = 0.0,
            ["thinking"] = false, ["use_default_template"] = true, ["mtp"] = "off"
        };
        graph["31"] = new { class_type = "TextGenerate", inputs = preparation };
        return preparation;
    }

    // The assistant reads the person's request verbatim, in its own language. The English
    // wrapper stays only in the fallback text HiDream receives when the assistant is off.
    private static string AssistantInput(VisualRequest r, string? referenceTitle)
    {
        var title = referenceTitle?.Trim();
        return string.IsNullOrEmpty(title) ? r.Prompt : "Referencia elegida: " + title + "\n" + r.Prompt;
    }

    private static string SystemPrompt(VisualRequest r, bool hasReference) => r.Task switch
    {
        VisualTask.Animation => VisualPrompts.Animation,
        VisualTask.Edit => VisualPrompts.Edit,
        _ => hasReference ? VisualPrompts.NewScene : VisualPrompts.Create
    };

    private static string Instruction(VisualRequest r, bool hasReference) => r.Task == VisualTask.Image && hasReference
        ? "Create a new scene featuring the subject from the reference image. Preserve their identity and characteristic appearance. Scene request: " + r.Prompt
        : r.Prompt;

    private static void Validate(VisualRequest r, string? prefix, string? reference)
    {
        r.Validate();
        if (prefix is not null && (prefix.Length != 32 || !prefix.All(Uri.IsHexDigit))) throw new InvalidDataException("Destino visual inválido.");
        if (reference is not null && (Path.GetFileName(reference) != reference || reference.IndexOfAny(['/', '\\', ':']) >= 0)) throw new InvalidDataException("Referencia visual inválida.");
        if (r.Task == VisualTask.Edit && reference is null) throw new InvalidDataException("Seleccioná una imagen para editar.");
    }
}
