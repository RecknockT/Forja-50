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

os.environ["FORJA_VISUAL_TOKEN"] = ""
try:
    spec.loader.exec_module(importlib.util.module_from_spec(spec))
    raise AssertionError("Se permitió cargar el puente sin credencial")
except RuntimeError:
    print("PASS puente visual: rechaza inicio sin credencial")
