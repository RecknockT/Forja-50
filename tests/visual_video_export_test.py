"""Validate the installed, pinned ComfyUI exporter with synthetic CPU frames only."""
import json
import sys
import tempfile
import types
from pathlib import Path

sys.dont_write_bytecode = True
comfy = Path(sys.argv[1]).resolve()
graph = json.loads(Path(sys.argv[2]).read_text(encoding="utf-8-sig"))
sys.path.insert(0, str(comfy))
sys.argv = ["forja-export-check", "--cpu"]
import torch
import av
import folder_paths
from comfy_api.latest import _io as io
from comfy_extras.nodes_video import CreateVideo, SaveVideo

assert not torch.cuda.is_initialized(), "The exporter test must not initialize CUDA"
live = dict(graph["14"]["inputs"])
live.pop("video")
_, _, dynamic = io.get_finalized_class_inputs(SaveVideo.INPUT_TYPES(), live)
inputs = io.build_nested_inputs(live, dynamic)
assert inputs["format"]["format"] == "mp4"
assert inputs["format"]["codec"]["codec"] == "h264"
with tempfile.TemporaryDirectory(prefix="Forja-video-export-") as temporary:
    folder_paths.set_output_directory(temporary)
    frames = torch.stack([torch.full((32, 32, 3), i / 7) for i in range(8)])
    video = CreateVideo.execute(images=frames, fps=24).result[0]
    # The executor normally supplies these optional metadata fields.
    SaveVideo.hidden = types.SimpleNamespace(extra_pnginfo=None, prompt=None)
    SaveVideo.execute(video=video, **inputs)
    files = list(Path(temporary).glob("*.mp4"))
    assert len(files) == 1
    with av.open(str(files[0])) as container:
        assert len(list(container.decode(video=0))) == 8
assert not torch.cuda.is_initialized()
print("PASS actual ComfyUI DynamicCombo and MP4/H264 export: 8 CPU frames, no CUDA")
