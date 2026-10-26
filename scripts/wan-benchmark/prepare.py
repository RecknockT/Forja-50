"""Fetch immutable asset metadata and an isolated GGUF loader; do not change installed packages."""
import argparse
import json
from pathlib import Path
import subprocess
import sys
import urllib.request

p=argparse.ArgumentParser();p.add_argument('root',type=Path);p.add_argument('--embedded-python',type=Path,required=True);a=p.parse_args()
root=a.root.resolve();root.mkdir(parents=True,exist_ok=True)
for owner,repo,revision in [
    ('lightx2v','LightWan2.2-A14B','f4b91eba1fd7dc2eb038191e2bca7ed33423032d'),
    ('QuantStack','Wan2.2-I2V-A14B-GGUF','6c6717459277b9cd1f72579d78a0fd62a79e57dc'),
    ('Comfy-Org','Wan-2.2','ee6f4a40737a995bf5818954cfce6d59443b0f04'),
]:
    with urllib.request.urlopen(f'https://huggingface.co/api/models/{owner}/{repo}/revision/{revision}?blobs=true',timeout=60) as response:meta=json.load(response)
    if meta['sha']!=revision:raise RuntimeError('Unexpected model revision')
    (root/(owner+'-metadata.json')).write_text(json.dumps(meta,indent=2),encoding='utf-8')
loader=root/'custom_nodes/ComfyUI-GGUF'
if not loader.exists():subprocess.run(['git','clone','--no-checkout','https://github.com/city96/ComfyUI-GGUF.git',str(loader)],check=True)
revision='6ea2651e7df66d7585f6ffee804b20e92fb38b8a'
subprocess.run(['git','-C',str(loader),'checkout',revision],check=True)
subprocess.run([str(a.embedded_python),'-s','-m','pip','install','--target',str(root/'deps'),'--no-deps','gguf==0.17.1'],check=True)
print('PASS metadata and isolated GGUF loader prepared')
