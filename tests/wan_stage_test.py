"""Exercise production expert residency and policy restoration without models/CUDA."""
from pathlib import Path
import sys
import types

events = []
device = types.SimpleNamespace(type="cuda")
capability = [12]
def load(*args, **options):
    events.append(("load", options.get("force_full_load")))
management = types.SimpleNamespace(get_torch_device=lambda: device,
    unload_all_models=lambda: events.append("unload"),
    soft_empty_cache=lambda: events.append("cache"), load_models_gpu=load)
sys.modules["comfy"] = types.ModuleType("comfy")
sys.modules["comfy.model_management"] = management
sys.modules["torch"] = types.SimpleNamespace(cuda=types.SimpleNamespace(get_device_capability=lambda _: (capability[0], 0)))

class Sampler:
    fail = False
    @classmethod
    def INPUT_TYPES(cls): return {"required": {}}
    def sample(self, **options):
        management.load_models_gpu([options["model"]], force_full_load=False)
        if self.fail: raise RuntimeError("simulated OOM")
        return ("latent",)
sys.modules["nodes"] = types.SimpleNamespace(KSamplerAdvanced=Sampler)
namespace = {"NODE_CLASS_MAPPINGS": {}}
source = Path(__file__).resolve().parents[1] / "src/MiIALocal.Core/VisualAssets/forja_animation.py"
exec(compile(source.read_text(encoding="utf-8"), str(source), "exec"), namespace)
stage = namespace["ForjaWanStage"]()
model = types.SimpleNamespace(loaded_size=lambda: 100, model_size=lambda: 100, lowvram_patch_counter=lambda: 0)
assert stage.sample_stage(sol=False, model=model) == ("latent",)
assert [event for event in events if isinstance(event, tuple)] == [("load", True), ("load", True)]
assert management.load_models_gpu is load and events[-2:] == ["unload", "cache"]
stage.fail = True
try: stage.sample_stage(sol=False, model=model)
except RuntimeError as error: assert str(error) == "simulated OOM"
else: raise AssertionError("Sampler error was hidden")
assert management.load_models_gpu is load and events[-2:] == ["unload", "cache"]
stage.fail = False
model.loaded_size = lambda: 99
try: stage.sample_stage(sol=False, model=model)
except RuntimeError as error: assert "VRAM" in str(error)
else: raise AssertionError("Partial residency was accepted")
assert management.load_models_gpu is load
model.loaded_size = lambda: 100
capability[0] = 8
try: stage.sample_stage(sol=False, model=model)
except RuntimeError: pass
else: raise AssertionError("NVFP4 was accepted on Ampere")
assert stage.sample_stage(sol=False, nvfp4=False, model=model) == ("latent",)
try: stage.sample_stage(sol=True, nvfp4=False, model=model)
except RuntimeError: pass
else: raise AssertionError("GGUF silently enabled Sol")
print("PASS Wan: complete expert loads, nested policy, error restoration, memory release and Ampere/GGUF routing")
