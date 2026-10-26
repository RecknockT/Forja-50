"""Wan A14B: one resident expert per stage; optional compiled Sol self-attention.

Appended to forja_guard inside its authenticated, single-queue Comfy runtime.
Normal and Sol share the same pinned NVFP4 checkpoints. No remote code or JIT.
"""
import gc
import nodes
import torch
import comfy.model_management as mm


class ForjaWanTextLoader:
    @classmethod
    def INPUT_TYPES(cls):
        import folder_paths
        return {"required": {"clip_name": (folder_paths.get_filename_list("text_encoders"),)}}

    RETURN_TYPES = ("CLIP",)
    FUNCTION = "load"
    CATEGORY = "Forja"

    def load(self, clip_name):
        import folder_paths
        import comfy.sd
        device = mm.get_torch_device()
        if device.type != "cuda":
            raise RuntimeError("El codificador de animación requiere la GPU NVIDIA.")
        mm.unload_all_models()
        mm.soft_empty_cache()
        return (comfy.sd.load_clip(
            ckpt_paths=[folder_paths.get_full_path_or_raise("text_encoders", clip_name)],
            embedding_directory=folder_paths.get_folder_paths("embeddings"),
            clip_type=comfy.sd.CLIPType.WAN,
            model_options={"load_device": device, "offload_device": torch.device("cpu")},
        ),)


def _forja_sol_model(model):
    import comfy_kitchen as kitchen
    from comfy.ldm.modules.attention import ComfyAttention, attention_pytorch, wrap_attn
    if not kitchen.sol_attn_is_available(mm.get_torch_device()):
        raise RuntimeError("Sol no está disponible en este motor. Elegí NVFP4 normal.")

    @wrap_attn
    def sol(q, k, v, heads, mask=None, attn_precision=None,
            skip_reshape=False, skip_output_reshape=False, **options):
        if mask is not None:
            return attention_pytorch(q, k, v, heads, mask=mask,
                attn_precision=attn_precision, skip_reshape=skip_reshape,
                skip_output_reshape=skip_output_reshape, **options)
        batch = q.shape[0]
        if skip_reshape:
            q, k, v = (tensor.transpose(1, 2) for tensor in (q, k, v))
        else:
            q, k, v = (tensor.reshape(batch, -1, heads, tensor.shape[-1] // heads)
                       for tensor in (q, k, v))
        output = kitchen.sol_attn(q, k, v, topk_ratio=0.1, tail=False,
                                  scale=options.get("scale"))
        return (output.transpose(1, 2) if skip_output_reshape
                else output.reshape(batch, -1, heads * output.shape[-1]))

    patched = model.clone()
    count = 0
    for index, block in enumerate(model.model.diffusion_model.blocks):
        if hasattr(block.self_attn, "comfy_attention"):
            attention = ComfyAttention()
            attention.function = sol
            patched.add_object_patch(
                f"diffusion_model.blocks.{index}.self_attn.comfy_attention", attention)
            count += 1
    if not count:
        raise RuntimeError("El modelo no admite Sol. Elegí NVFP4 normal.")
    return patched


class ForjaWanStage(nodes.KSamplerAdvanced):
    @classmethod
    def INPUT_TYPES(cls):
        schema = super().INPUT_TYPES()
        schema["required"]["sol"] = ("BOOLEAN", {"default": True})
        schema["required"]["nvfp4"] = ("BOOLEAN", {"default": True})
        return schema

    FUNCTION = "sample_stage"
    CATEGORY = "Forja"

    def sample_stage(self, sol, nvfp4=True, **kwargs):
        device = mm.get_torch_device()
        if device.type != "cuda" or (nvfp4 and torch.cuda.get_device_capability(device)[0] < 12):
            raise RuntimeError("La animación NVFP4 requiere una RTX 5070 Ti o 5080.")
        if sol and not nvfp4:
            raise RuntimeError("Sol solo está habilitado para el perfil NVFP4 de RTX 50.")
        model = _forja_sol_model(kwargs["model"]) if sol else kwargs["model"]
        kwargs["model"] = model
        # Free the encoder/previous expert before the next complete expert loads.
        mm.unload_all_models()
        gc.collect()
        mm.soft_empty_cache()
        original_load = mm.load_models_gpu

        def fully_load(*args, **options):
            options["force_full_load"] = True
            return original_load(*args, **options)

        # Comfy prepares memory again inside sample(); preloading alone is insufficient.
        # The private runtime has a single worker queue; always restore global policy.
        mm.load_models_gpu = fully_load
        try:
            mm.load_models_gpu([model])
            result = super().sample(**kwargs)
            if model.loaded_size() < model.model_size() or model.lowvram_patch_counter():
                raise RuntimeError("El experto no quedó completo en VRAM. Liberá memoria de GPU y reintentá.")
            return result
        finally:
            mm.load_models_gpu = original_load
            mm.unload_all_models()
            mm.soft_empty_cache()


NODE_CLASS_MAPPINGS["ForjaWanStage"] = ForjaWanStage
NODE_CLASS_MAPPINGS["ForjaWanTextLoader"] = ForjaWanTextLoader
