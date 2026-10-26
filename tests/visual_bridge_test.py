"""Exercise the actual embedded bridge without ComfyUI, CUDA or model weights."""
import asyncio
import importlib.util
import os
from pathlib import Path
import sys
import tempfile
import types
sys.dont_write_bytecode = True

source = Path(__file__).resolve().parents[1] / "src/MiIALocal.Core/VisualAssets/forja_guard.py"

class Response:
    def __init__(self, status=200, text=""):
        self.status, self.text = status, text

app = types.SimpleNamespace(middlewares=[])
routes = types.SimpleNamespace(get=lambda _: lambda handler: handler)
sys.modules["server"] = types.SimpleNamespace(PromptServer=types.SimpleNamespace(instance=types.SimpleNamespace(app=app, routes=routes)))
sys.modules["aiohttp"] = types.SimpleNamespace(web=types.SimpleNamespace(middleware=lambda handler: handler, Response=Response, json_response=lambda _: Response()))
os.environ["FORJA_VISUAL_TOKEN"] = "a" * 64
spec = importlib.util.spec_from_file_location("forja_guard_under_test", source)
bridge = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bridge)

async def checks():
    called = 0
    async def handler(_):
        nonlocal called
        called += 1
        return Response()
    for headers in ({}, {"Authorization": "Bearer wrong"}, {"Authorization": "Bearer " + "a" * 64, "Origin": "http://127.0.0.1"}):
        result = await bridge._forja_guard(types.SimpleNamespace(headers=headers), handler)
        assert result.status == 403
    assert called == 0
    result = await bridge._forja_guard(types.SimpleNamespace(headers={"Authorization": "Bearer " + "a" * 64}), handler)
    assert result.status == 200 and called == 1 and len(app.middlewares) == 1

asyncio.run(checks())
print("PASS puente visual: autenticación real, rechazo de credenciales incorrectas y orígenes de navegador")

unloaded = []
management = types.SimpleNamespace(unload_all_models=lambda: unloaded.append("unload"), soft_empty_cache=lambda: unloaded.append("cache"))
sys.modules["comfy"] = types.SimpleNamespace(model_management=management)
sys.modules["comfy.model_management"] = management
prepared = bridge.ForjaPreparedPrompt().prepare("  A character flying above a forest.  ")
assert prepared["result"][0] == "A character flying above a forest." and unloaded == ["unload", "cache"]
try:
    bridge.ForjaPreparedPrompt().prepare(" ")
    raise AssertionError("Se aceptó un pedido preparado vacío")
except ValueError:
    pass
print("PASS preparación visual: descripción registrada, liberación del asistente y rechazo de salida vacía")

# Reproduce the LOW_VRAM policy: generic encoders would run on CPU. Execute our
# real loader against a test adapter and verify both selected GPU and offload.
selected_gpu = types.SimpleNamespace(type="cuda", index=2)
cpu = types.SimpleNamespace(type="cpu")
management.get_torch_device = lambda: selected_gpu
management.text_encoder_device = lambda: cpu
loaded = []
sd = types.SimpleNamespace(CLIPType=types.SimpleNamespace(STABLE_DIFFUSION="fixture"), load_clip=lambda **kw: loaded.append(kw) or "clip")
sys.modules["comfy"].sd = sd
sys.modules["comfy.sd"] = sd
sys.modules["folder_paths"] = types.SimpleNamespace(get_full_path_or_raise=lambda category, name: "/models/" + name, get_folder_paths=lambda _: [], get_filename_list=lambda _: ["assistant.safetensors"])
original_torch = sys.modules.get("torch")
sys.modules["torch"] = types.SimpleNamespace(device=lambda name: cpu if name == "cpu" else selected_gpu)
unloaded.clear()
assert bridge.ForjaAssistantLoader().load("assistant.safetensors") == ("clip",)
assert loaded[0]["model_options"]["load_device"] is selected_gpu
assert loaded[0]["model_options"]["offload_device"] is cpu
assert unloaded == ["unload", "cache"]
management.get_torch_device = lambda: cpu
try:
    bridge.ForjaAssistantLoader().load("assistant.safetensors")
    raise AssertionError("Se ejecutó el asistente silenciosamente en CPU")
except RuntimeError:
    pass
assert len(loaded) == 1
if original_torch is None:
    del sys.modules["torch"]
else:
    sys.modules["torch"] = original_torch
print("PASS asistente: GPU seleccionada incluso con LOW_VRAM, offload CPU y liberación entre etapas")

if "--real-gif" in sys.argv:
    import numpy as np
    from PIL import Image
    class Frame:
        def __init__(self, value): self.value = value
        def cpu(self): return self
        def numpy(self): return self.value
    with tempfile.TemporaryDirectory(prefix="Forja-gif-test-") as temporary:
        sys.modules["folder_paths"] = types.SimpleNamespace(get_output_directory=lambda: temporary, get_save_image_path=lambda prefix, _: (temporary, prefix, 1, "", prefix))
        frames = [Frame(np.full((8, 8, 3), i / 7, dtype=np.float32)) for i in range(8)]
        info = bridge.ForjaSaveGif().save(frames, "b" * 32, 24)
        file = Path(temporary) / info["ui"]["gifs"][0]["filename"]
        with Image.open(file) as gif:
            assert gif.n_frames == 8 and gif.info["loop"] == 0
        assert not list(Path(temporary).glob("*.tmp"))
    print("PASS GIF real: ocho fotogramas, repetición y escritura atómica; sin GPU")

if "--real-fit" in sys.argv:
    import torch
    assert not torch.cuda.is_initialized()
    image = torch.zeros((1, 4, 8, 3))
    image[:, :, :4, 0] = 1
    image[:, :, 4:, 2] = 1
    fitted = bridge.ForjaFitReference().fit(image, 256, 256)[0]
    assert fitted.shape == (1, 256, 256, 3)
    assert fitted[0, 0, 0, 0] == 1 and fitted[0, -1, -1, 2] == 1
    assert torch.allclose(fitted[:, 0], fitted[:, 64])
    assert bridge.ForjaFitReference().fit(image, 1024, 1024)[0].shape == (1, 1024, 1024, 3)
    limits = bridge.ForjaFitReference.INPUT_TYPES()["required"]
    assert limits["width"][1]["max"] >= 3104 and limits["height"][1]["max"] >= 3104, "El nodo limita el lienzo entrenado de HiDream y Comfy rechaza el grafo"
    assert bridge.ForjaFitReference().fit(image, 2560, 1440)[0].shape == (1, 1440, 2560, 3)
    gradient = torch.linspace(0, 1, 48).view(1, 1, 48, 1).expand(1, 32, 48, 3)
    animated = bridge.ForjaFitReference().fit(gradient, 256, 256, "animation")[0]
    assert animated.shape == (1, 256, 256, 3)
    assert torch.all(torch.diff(animated[0, 128, :, 0]) > 0), "La animación repite columnas de relleno"
    assert animated[0, 128, 0, 0] > .05 and animated[0, 128, -1, 0] < .95, "El formato fijo no recorta el exceso"
    assert not torch.cuda.is_initialized()
    print("PASS referencia real: marco completo y orientación sin recorte; CPU sin CUDA")

os.environ["FORJA_VISUAL_TOKEN"] = ""
try:
    spec.loader.exec_module(importlib.util.module_from_spec(spec))
    raise AssertionError("Se permitió cargar el puente sin credencial")
except RuntimeError:
    print("PASS puente visual: rechaza inicio sin credencial")
