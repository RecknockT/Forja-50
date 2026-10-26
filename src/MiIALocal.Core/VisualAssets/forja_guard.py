"""Forja-owned ComfyUI authentication and local GIF export. No network services."""
import hmac
import os
from aiohttp import web
from server import PromptServer

_token = os.environ.get("FORJA_VISUAL_TOKEN", "")
if len(_token) != 64:
    raise RuntimeError("Forja: missing local runtime credential")

@web.middleware
async def _forja_guard(request, handler):
    if request.headers.get("Origin") or not hmac.compare_digest(
        request.headers.get("Authorization", ""), "Bearer " + _token
    ):
        return web.Response(status=403, text="Forja: access denied")
    return await handler(request)

PromptServer.instance.app.middlewares.append(_forja_guard)

@PromptServer.instance.routes.get("/forja/health")
async def _forja_health(request):
    return web.json_response({"forja": True, "version": 1})

class ForjaSaveGif:
    @classmethod
    def INPUT_TYPES(cls):
        return {"required": {"images": ("IMAGE",), "filename_prefix": ("STRING",), "fps": ("INT", {"default": 24, "min": 1, "max": 30})}}
    RETURN_TYPES = ()
    FUNCTION = "save"
    OUTPUT_NODE = True
    CATEGORY = "Forja"

    def save(self, images, filename_prefix, fps):
        import folder_paths
        import numpy as np
        from PIL import Image
        if len(filename_prefix) != 32 or any(c not in "0123456789abcdef" for c in filename_prefix):
            raise ValueError("Invalid Forja output identifier")
        if len(images) > 121:
            raise ValueError("Frame limit exceeded")
        folder, name, counter, subfolder, _ = folder_paths.get_save_image_path(filename_prefix, folder_paths.get_output_directory())
        target = f"{name}_{counter:05}_.gif"
        frames = [Image.fromarray((frame.cpu().numpy() * 255).clip(0, 255).astype(np.uint8)).convert("P", palette=Image.Palette.ADAPTIVE) for frame in images]
        destination = os.path.join(folder, target)
        temporary = destination + ".tmp"
        try:
            durations = [round((i + 1) * 100 / fps) * 10 - round(i * 100 / fps) * 10 for i in range(len(frames))]
            frames[0].save(temporary, format="GIF", save_all=True, append_images=frames[1:], duration=durations, loop=0, disposal=2, optimize=False)
            os.replace(temporary, destination)
        finally:
            if os.path.exists(temporary):
                os.remove(temporary)
            for frame in frames:
                frame.close()
        return {"ui": {"gifs": [{"filename": target, "subfolder": subfolder, "type": "output"}]}}

class ForjaFitReference:
    @classmethod
    def INPUT_TYPES(cls):
        return {"required": {"image": ("IMAGE",), "width": ("INT", {"min": 256, "max": 3104}), "height": ("INT", {"min": 256, "max": 3104})}, "optional": {"fit_mode": (["preserve", "animation"], {"default": "preserve"})}}
    RETURN_TYPES = ("IMAGE",)
    FUNCTION = "fit"
    CATEGORY = "Forja"

    def fit(self, image, width, height, fit_mode="preserve"):
        import torch
        import torch.nn.functional as functional
        if width < 256 or height < 256 or width > 3104 or height > 3104:
            raise ValueError("Unsupported reference dimensions")
        pixels = image[:1, :, :, :3].movedim(-1, 1).cpu()
        if fit_mode == "animation":
            # The automatic canvas follows the source aspect. Fixed formats use a
            # centered crop, never stretched/replicated strips as conditioning.
            scale = max(width / pixels.shape[-1], height / pixels.shape[-2])
            resized_width = max(width, round(pixels.shape[-1] * scale))
            resized_height = max(height, round(pixels.shape[-2] * scale))
            pixels = functional.interpolate(pixels, size=(resized_height, resized_width), mode="bicubic", align_corners=False, antialias=True)
            left = (resized_width - width) // 2; top = (resized_height - height) // 2
            return (pixels[:, :, top:top+height, left:left+width].clamp(0, 1).movedim(1, -1),)
        if fit_mode != "preserve": raise ValueError("Unsupported reference fit mode")
        # Image editing keeps its existing full-frame letterboxing policy.
        scale = min(width / pixels.shape[-1], height / pixels.shape[-2])
        resized_width = max(1, min(width, round(pixels.shape[-1] * scale)))
        resized_height = max(1, min(height, round(pixels.shape[-2] * scale)))
        pixels = functional.interpolate(pixels, size=(resized_height, resized_width), mode="bilinear", align_corners=False)
        left = (width - resized_width) // 2
        top = (height - resized_height) // 2
        pixels = functional.pad(pixels, (left, width - resized_width - left, top, height - resized_height - top), mode="replicate")
        return (pixels.movedim(1, -1),)

class ForjaAssistantLoader:
    @classmethod
    def INPUT_TYPES(cls):
        import folder_paths
        return {"required": {"clip_name": (folder_paths.get_filename_list("text_encoders"),)}}
    RETURN_TYPES = ("CLIP",)
    FUNCTION = "load"
    CATEGORY = "Forja"

    def load(self, clip_name):
        import torch
        import folder_paths
        import comfy.sd
        import comfy.model_management
        device = comfy.model_management.get_torch_device()
        if device.type != "cuda":
            raise RuntimeError("El asistente visual requiere la GPU NVIDIA; no se ejecutará lentamente en CPU.")
        # LOW_VRAM sends generic text encoders to CPU. Override only this stage,
        # retaining the memory-saving policy used by Wan and diffusion.
        comfy.model_management.unload_all_models()
        comfy.model_management.soft_empty_cache()
        clip = comfy.sd.load_clip(
            ckpt_paths=[folder_paths.get_full_path_or_raise("text_encoders", clip_name)],
            embedding_directory=folder_paths.get_folder_paths("embeddings"),
            clip_type=comfy.sd.CLIPType.STABLE_DIFFUSION,
            model_options={"load_device": device, "offload_device": torch.device("cpu")},
        )
        return (clip,)

class ForjaPreparedPrompt:
    @classmethod
    def INPUT_TYPES(cls):
        return {"required": {"text": ("STRING", {"forceInput": True})}}
    RETURN_TYPES = ("STRING",)
    FUNCTION = "prepare"
    OUTPUT_NODE = True
    CATEGORY = "Forja"

    def prepare(self, text):
        import comfy.model_management
        text = text.strip()
        if not text or len(text) > 12000:
            raise ValueError("Visual stage returned an empty or oversized prepared prompt")
        # Finish the VLM stage before loading diffusion components into GPU memory.
        comfy.model_management.unload_all_models()
        comfy.model_management.soft_empty_cache()
        return {"ui": {"text": [text]}, "result": (text,)}

NODE_CLASS_MAPPINGS = {"ForjaSaveGif": ForjaSaveGif, "ForjaFitReference": ForjaFitReference, "ForjaPreparedPrompt": ForjaPreparedPrompt, "ForjaAssistantLoader": ForjaAssistantLoader}
