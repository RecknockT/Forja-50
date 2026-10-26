"""Manual benchmark assets only. Pinned HF metadata, resumable ranges, SHA-256 validation."""
import concurrent.futures as futures
import hashlib
import json
import os
from pathlib import Path
import sys
import threading
import time
import urllib.request

root = Path(sys.argv[1]).resolve()
models = root / "models"
models.mkdir(exist_ok=True)
assets = []
for owner, repo, names, folder in [
    ("lightx2v", "LightWan2.2-A14B", ["Wan2.2-I2V-A14B_NVFP4_Sparse_high_comfy.safetensors", "Wan2.2-I2V-A14B_NVFP4_Sparse_low_comfy.safetensors"], "diffusion_models"),
    ("QuantStack", "Wan2.2-I2V-A14B-GGUF", ["HighNoise/Wan2.2-I2V-A14B-HighNoise-Q4_K_S.gguf", "LowNoise/Wan2.2-I2V-A14B-LowNoise-Q4_K_S.gguf"], "diffusion_models"),
    ("Comfy-Org", "Wan-2.2", ["split_files/loras/wan2.2_i2v_lightx2v_4steps_lora_v1_high_noise.safetensors", "split_files/loras/wan2.2_i2v_lightx2v_4steps_lora_v1_low_noise.safetensors", "split_files/vae/wan_2.1_vae.safetensors"], None),
]:
    meta = json.loads((root / (owner + "-metadata.json")).read_text(encoding="utf-8-sig"))
    for name in names:
        item = next(x for x in meta["siblings"] if x["rfilename"] == name)
        target_folder = folder or name.split("/")[1]
        assets.append(dict(repo=f"{owner}/{repo}", revision=meta["sha"], file=name, size=item["size"], sha256=item["lfs"]["sha256"], target=f"{target_folder}/{Path(name).name}"))
(root / "assets.json").write_text(json.dumps(assets, indent=2), encoding="utf-8")
lock = threading.Lock()
transferred = 0
started = time.monotonic()
last = started
chunk_size = 64 * 1024**2

def sha(path):
    with path.open("rb") as f:
        return hashlib.file_digest(f, "sha256").hexdigest()

def chunk(asset, index):
    global transferred, last
    target = models / asset["target"]
    partials = Path(str(target) + ".chunks")
    partials.mkdir(parents=True, exist_ok=True)
    start = index * chunk_size
    end = min(asset["size"], start + chunk_size) - 1
    part = partials / str(index)
    if part.exists() and part.stat().st_size == end - start + 1:
        return
    for attempt in range(4):
        try:
            request = urllib.request.Request(f'https://huggingface.co/{asset["repo"]}/resolve/{asset["revision"]}/{asset["file"]}?download=true', headers={"Range": f"bytes={start}-{end}", "User-Agent": "Forja50-benchmark/1"})
            with urllib.request.urlopen(request, timeout=60) as response:
                if response.status != 206 or response.headers.get("Content-Range") != f'bytes {start}-{end}/{asset["size"]}':
                    raise RuntimeError("Range response mismatch")
                with part.with_suffix(".tmp").open("wb") as output:
                    while block := response.read(1024**2):
                        output.write(block)
                        with lock:
                            transferred += len(block)
                            now = time.monotonic()
                            if now - last > 15:
                                print(f"Downloaded {transferred / 1e9:.2f} GB; average {transferred / (now - started) / 1024**2:.1f} MiB/s", flush=True)
                                last = now
            if part.with_suffix(".tmp").stat().st_size != end - start + 1:
                raise RuntimeError("Incomplete chunk")
            os.replace(part.with_suffix(".tmp"), part)
            return
        except Exception:
            if attempt == 3:
                raise
            time.sleep(2**attempt)

tasks = []
for asset in assets:
    target = models / asset["target"]
    target.parent.mkdir(exist_ok=True)
    if target.exists() and target.stat().st_size == asset["size"] and sha(target) == asset["sha256"]:
        print("Reused", target.name, flush=True)
        continue
    tasks.extend((asset, index) for index in range((asset["size"] + chunk_size - 1) // chunk_size))
with futures.ThreadPoolExecutor(max_workers=16) as pool:
    list(pool.map(lambda args: chunk(*args), tasks))
for asset in assets:
    target = models / asset["target"]
    if target.exists() and target.stat().st_size == asset["size"] and sha(target) == asset["sha256"]:
        continue
    assembled = Path(str(target) + ".assembling")
    with assembled.open("wb") as output:
        for index in range((asset["size"] + chunk_size - 1) // chunk_size):
            with (Path(str(target) + ".chunks") / str(index)).open("rb") as source:
                while block := source.read(8 * 1024**2):
                    output.write(block)
    if assembled.stat().st_size != asset["size"] or sha(assembled) != asset["sha256"]:
        raise RuntimeError("SHA-256 mismatch: " + target.name)
    os.replace(assembled, target)
    # Remove only chunks created for this exact, verified asset.
    partials = Path(str(target) + ".chunks")
    for part in partials.iterdir():
        if part.is_file() and (part.name.isdigit() or part.suffix == ".tmp"):
            part.unlink()
    partials.rmdir()
    print("Verified", target.name, flush=True)
print("PASS: all assets verified", flush=True)
