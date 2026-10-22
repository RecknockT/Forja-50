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
        return {"required": {"image": ("IMAGE",), "width": ("INT", {"min": 256, "max": 2048}), "height": ("INT", {"min": 256, "max": 2048})}}
    RETURN_TYPES = ("IMAGE",)
    FUNCTION = "fit"
    CATEGORY = "Forja"

    def fit(self, image, width, height):
        import torch
        import torch.nn.functional as functional
        if width < 256 or height < 256 or width > 2048 or height > 2048:
            raise ValueError("Unsupported reference dimensions")
        # Letterbox the entire initial frame instead of the Wan node's center crop.
        pixels = image[:1, :, :, :3].movedim(-1, 1).cpu()
        scale = min(width / pixels.shape[-1], height / pixels.shape[-2])
        resized_width = max(1, min(width, round(pixels.shape[-1] * scale)))
        resized_height = max(1, min(height, round(pixels.shape[-2] * scale)))
        pixels = functional.interpolate(pixels, size=(resized_height, resized_width), mode="bilinear", align_corners=False)
        left = (width - resized_width) // 2
        top = (height - resized_height) // 2
        pixels = functional.pad(pixels, (left, width - resized_width - left, top, height - resized_height - top), mode="replicate")
        return (pixels.movedim(1, -1),)

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

NODE_CLASS_MAPPINGS = {"ForjaSaveGif": ForjaSaveGif, "ForjaFitReference": ForjaFitReference, "ForjaPreparedPrompt": ForjaPreparedPrompt}
