"""Manual, opt-in GPU benchmark. Reuses installed weights; never changes Forja settings.

Run with the existing Comfy embedded Python:
  run.py BENCH_ROOT DATA_ROOT --allow-gpu --variants 5b,gguf,nvfp4 --repeats 3
Timing includes prompt encoding, stage loading, sampling, decode and export; startup is separate.
"""
import argparse
import ctypes as c
import hashlib
import json
import os
from pathlib import Path
import secrets
import shutil
import socket
import subprocess
import sys
import threading
import time
import urllib.request
import uuid
import psutil

parser=argparse.ArgumentParser()
parser.add_argument("root",type=Path); parser.add_argument("data",type=Path)
parser.add_argument("--allow-gpu",action="store_true")
parser.add_argument("--variants",default="5b,gguf,nvfp4")
parser.add_argument("--repeats",type=int,default=3)
parser.add_argument("--width",type=int,default=832); parser.add_argument("--height",type=int,default=480)
parser.add_argument("--frames",type=int,default=81)
parser.add_argument("--disable-pinned-memory",action="store_true")
parser.add_argument("--reference",type=Path)
parser.add_argument("--production-graphs",type=Path,help="Directory with wan-normal-graph.json and wan-sol-graph.json exported by app tests")
args=parser.parse_args()
if not args.allow_gpu: raise RuntimeError("GPU benchmark requires --allow-gpu")
root=args.root.resolve(); data=args.data.resolve()
engine=data/'runtime/comfy/ComfyUI_windows_portable'
script=Path(__file__).resolve().parent
run=root/('run-'+time.strftime('%Y%m%d-%H%M%S'));run.mkdir(parents=True)
for folder in ['input','output','user','temp']: (run/folder).mkdir()
# Existing, clothed anime reference; never pull an arbitrary latest user creation.
reference=args.reference or root/'reference.png'
if not reference.exists(): raise FileNotFoundError("Supply the benchmark reference input before running")
shutil.copy2(reference,run/'input/reference.png')
(run/'experiment.json').write_text(json.dumps(dict(width=args.width,height=args.height,frames=args.frames,fps=24,seed=20261026,repeats=args.repeats,disable_pinned_memory=args.disable_pinned_memory,reference_sha256=hashlib.sha256(reference.read_bytes()).hexdigest(),attention='PyTorch SDPA (dense); not the LightX2V sparse-attention runtime',nvfp4='LightWan converted Comfy checkpoints',fully_resident_stage_requested=True),indent=2),encoding='utf-8')
custom=root/'custom_nodes';custom.mkdir(exist_ok=True)
for name,source in [('forja_guard',script.parents[1]/'src/MiIALocal.Core/VisualAssets/forja_guard.py'),('forja_benchmark',script/'stages.py')]:
    target=custom/name;target.mkdir(exist_ok=True);shutil.copy2(source,target/'__init__.py')
    if name=='forja_guard' and args.production_graphs:
        with (target/'__init__.py').open('a',encoding='utf-8') as output:
            output.write('\n'+(script.parents[1]/'src/MiIALocal.Core/VisualAssets/forja_animation.py').read_text(encoding='utf-8'))
config={'shared':{'base_path':str(data/'visual/models'),'diffusion_models':'diffusion_models','text_encoders':'text_encoders','vae':'vae'}}
(root/'models.json').write_text(json.dumps(config),encoding='utf-8')
prompt="The white-haired elf mage gently moves both hands outward, creating a bright blue magical ring. Blue particles orbit her hands. Her hair and cape sway naturally. Preserve her face, outfit, proportions and the forest background. Continuous smooth motion, fixed camera, no cuts."
negative="static, motionless, blurry, deformed hands, extra fingers, extra limbs, changing face, changing clothes, flickering, subtitles, text, watermark, camera cuts"
def graph(variant,prefix,seed):
    if args.production_graphs:
        if variant not in ['nvfp4','nvfp4-sol']:raise ValueError('Production validation only supports NVFP4')
        g=json.loads((args.production_graphs/('wan-sol-graph.json' if variant=='nvfp4-sol' else 'wan-normal-graph.json')).read_text(encoding='utf-8'))
        for node in g.values():
            inputs=node['inputs']
            if node['class_type']=='LoadImage':inputs['image']='reference.png'
            if 'filename_prefix' in inputs:inputs['filename_prefix']=prefix
            if 'noise_seed' in inputs:inputs['noise_seed']=seed
        return g
    if variant=='nvfp4-sol':variant='nvfp4'
    g={}
    def n(i,kind,**inputs):g[str(i)]={'class_type':kind,'inputs':inputs}
    def l(i,slot=0):return [str(i),slot]
    n(20,'LoadImage',image='reference.png');n(21,'ForjaFitReference',image=l(20),width=args.width,height=args.height)
    n(2,'CLIPLoader',clip_name='umt5_xxl_fp8_e4m3fn_scaled.safetensors',type='wan',device='default')
    n(4,'CLIPTextEncode',clip=l(2),text=prompt);n(5,'CLIPTextEncode',clip=l(2),text=negative)
    n(3,'VAELoader',vae_name='wan2.2_vae.safetensors' if variant=='5b' else 'wan_2.1_vae.safetensors')
    if variant=='5b':
        n(1,'UNETLoader',unet_name='wan2.2_ti2v_5B_fp16.safetensors',weight_dtype='default')
        n(6,'Wan22ImageToVideoLatent',vae=l(3),start_image=l(21),width=args.width,height=args.height,length=args.frames,batch_size=1)
        n(7,'ModelSamplingSD3',model=l(1),shift=8.0)
        n(11,'ForjaBenchmarkStage',stage='5b',model=l(7),add_noise='enable',noise_seed=seed,steps=20,cfg=5.0,sampler_name='uni_pc',scheduler='simple',positive=l(4),negative=l(5),latent_image=l(6),start_at_step=0,end_at_step=20,return_with_leftover_noise='disable')
    else:
        n(6,'WanImageToVideo',positive=l(4),negative=l(5),vae=l(3),start_image=l(21),width=args.width,height=args.height,length=args.frames,batch_size=1)
        for i,label in [(1,'high'),(10,'low')]:
            if variant=='gguf':
                n(i,'UnetLoaderGGUFAdvanced',unet_name=f'Wan2.2-I2V-A14B-{label.title()}Noise-Q4_K_S.gguf',dequant_dtype='bfloat16',patch_dtype='bfloat16',patch_on_device=True)
                n(i+30,'LoraLoaderModelOnly',model=l(i),lora_name=f'wan2.2_i2v_lightx2v_4steps_lora_v1_{label}_noise.safetensors',strength_model=1.0)
                model=l(i+30)
            else:
                n(i,'UNETLoader',unet_name=f'Wan2.2-I2V-A14B_NVFP4_Sparse_{label}_comfy.safetensors',weight_dtype='default');model=l(i)
            n(i+40,'ModelSamplingSD3',model=model,shift=5.0 if variant=='gguf' else 7.0)
        # Separate stage nodes guarantee the high expert leaves GPU before loading low.
        for i,label,start,end in [(11,'high',0,2),(12,'low',2,4)]:
            n(i,'ForjaBenchmarkStage',stage=label,model=l(41 if label=='high' else 50),add_noise='enable' if start==0 else 'disable',noise_seed=seed,steps=4,cfg=1.0,sampler_name='euler',scheduler='simple',positive=l(6),negative=l(6,1),latent_image=l(6,2) if start==0 else l(11),start_at_step=start,end_at_step=end,return_with_leftover_noise='enable' if start==0 else 'disable')
    n(60,'VAEDecode',samples=l(11 if variant=='5b' else 12),vae=l(3))
    n(61,'CreateVideo',images=l(60),fps=24.0)
    n(62,'SaveVideo',video=l(61),filename_prefix=prefix,**{'format':'mp4','format.codec':'h264'})
    return g

class Memory(c.Structure):_fields_=[('total',c.c_ulonglong),('free',c.c_ulonglong),('used',c.c_ulonglong)]
class Util(c.Structure):_fields_=[('gpu',c.c_uint),('memory',c.c_uint)]
nv=c.WinDLL('nvml.dll');assert nv.nvmlInit_v2()==0
handle=c.c_void_p();assert nv.nvmlDeviceGetHandleByIndex_v2(0,c.byref(handle))==0
def gpu_stats():
    m=Memory();u=Util();power=c.c_uint();temp=c.c_uint()
    assert nv.nvmlDeviceGetMemoryInfo(handle,c.byref(m))==0
    nv.nvmlDeviceGetUtilizationRates(handle,c.byref(u));nv.nvmlDeviceGetPowerUsage(handle,c.byref(power));nv.nvmlDeviceGetTemperature(handle,0,c.byref(temp))
    row=dict(gpu_used=m.used,gpu_free=m.free,gpu_util=u.gpu,power_w=power.value/1000,temperature_c=temp.value)
    for counter,label in [(0,'pcie_tx_kib_s'),(1,'pcie_rx_kib_s')]:
        value=c.c_uint()
        if nv.nvmlDeviceGetPcieThroughput(handle,counter,c.byref(value))==0:row[label]=value.value
    return row
reports=[]
print('RESULTS',run,flush=True)
for variant in args.variants.split(','):
    if variant not in ['5b','gguf','nvfp4','nvfp4-sol']:raise ValueError(variant)
    variant_root=run/variant;variant_root.mkdir()
    stage_file=variant_root/'stages.jsonl'
    token=secrets.token_hex(32)
    with socket.socket() as s:s.bind(('127.0.0.1',0));port=s.getsockname()[1]
    env=os.environ.copy();env.update(FORJA_VISUAL_TOKEN=token,FORJA_BENCH_STAGES=str(stage_file),PYTHONUTF8='1',PYTHONNOUSERSITE='1',PYTHONDONTWRITEBYTECODE='1',HF_HUB_OFFLINE='1',TRANSFORMERS_OFFLINE='1')
    env.pop('FORJA_BENCH_SOL_TOPK',None)
    if variant=='nvfp4-sol':env['FORJA_BENCH_SOL_TOPK']='0.1'
    command=[str(engine/'python_embeded/python.exe'),'-s',str(script/'launch.py'),str(root),str(engine/'ComfyUI'),'--base-directory',str(root),'--user-directory',str(run/'user'),'--temp-directory',str(run/'temp'),'--output-directory',str(run/'output'),'--input-directory',str(run/'input'),'--extra-model-paths-config',str(root/'models.json'),'--listen','127.0.0.1','--port',str(port),'--cuda-device','0','--disable-auto-launch','--windows-standalone-build','--disable-all-custom-nodes','--whitelist-custom-nodes','forja_guard','forja_benchmark','ComfyUI-GGUF','--offline','--disable-assets','--cache-none','--disable-dynamic-vram','--reserve-vram','1.0']
    if args.disable_pinned_memory: command.append('--disable-pinned-memory')
    if args.production_graphs:
        command.extend(['--lowvram'])
        command[command.index('--reserve-vram')+1]='1.5'
    def api(path,body=None):
        req=urllib.request.Request(f'http://127.0.0.1:{port}/'+path,data=None if body is None else json.dumps(body).encode(),headers={'Authorization':'Bearer '+token,'Content-Type':'application/json'})
        with urllib.request.urlopen(req,timeout=30) as r:return json.load(r)
    baseline=[gpu_stats() for _ in range(5)]
    with (variant_root/'engine.log').open('wb') as log:
        started=time.monotonic();worker=subprocess.Popen(command,cwd=engine,env=env,stdout=log,stderr=log,creationflags=subprocess.CREATE_NO_WINDOW)
        (variant_root/'process.json').write_text(json.dumps(dict(pid=worker.pid,command=command,started=time.time()),indent=2),encoding='utf-8')
        try:
            deadline=time.monotonic()+180
            while True:
                if worker.poll() is not None:raise RuntimeError('Engine failed; see '+str(variant_root/'engine.log'))
                try:api('forja/health');break
                except Exception:
                    if time.monotonic()>deadline:raise
                    time.sleep(1)
            startup=time.monotonic()-started
            for repeat in range(args.repeats):
                prefix=uuid.uuid4().hex;g=graph(variant,prefix,20261026)
                (variant_root/f'graph-{repeat}.json').write_text(json.dumps(g,indent=2),encoding='utf-8')
                samples=[];stop=threading.Event()
                def monitor():
                    process=psutil.Process(worker.pid)
                    while not stop.is_set():
                        row=dict(time=time.time(),**gpu_stats(),ram_available=psutil.virtual_memory().available)
                        try:
                            mem=process.memory_info();row.update(rss=mem.rss,private=getattr(mem,'private',0),pagefile=getattr(mem,'pagefile',0))
                        except psutil.Error:pass
                        samples.append(row);stop.wait(.2)
                monitoring=threading.Thread(target=monitor,daemon=True);monitoring.start()
                started=time.monotonic();start_wall=time.time();error=None;history=None
                try:
                    accepted=api('prompt',{'prompt':g});pid=accepted['prompt_id'];deadline=time.monotonic()+1200
                    while True:
                        history=api('history/'+pid).get(pid)
                        if history:
                            status=history.get('status',{})
                            if status.get('status_str')=='error':raise RuntimeError(json.dumps(status))
                            if status.get('completed'):break
                        if worker.poll() is not None:raise RuntimeError('Worker exited')
                        if time.monotonic()>deadline:raise TimeoutError('Generation exceeded 20 minutes')
                        time.sleep(.25)
                except Exception as ex:error=repr(ex)
                finally:stop.set();monitoring.join()
                elapsed=time.monotonic()-started
                stages=[json.loads(line) for line in stage_file.read_text(encoding='utf-8').splitlines() if json.loads(line)['time']>=start_wall] if stage_file.exists() else []
                report=dict(variant=variant,repeat=repeat,seconds=elapsed,startup_seconds=startup,first_in_process=repeat==0,error=error,prefix=prefix,baseline_gpu_used=baseline[-1]['gpu_used'],gpu_peak=max(x['gpu_used'] for x in samples),ram_rss_peak=max(x.get('rss',0) for x in samples),ram_private_peak=max(x.get('private',0) for x in samples),ram_available_min=min(x['ram_available'] for x in samples),stages=stages)
                if history:(variant_root/f'history-{repeat}.json').write_text(json.dumps(history,indent=2),encoding='utf-8')
                (variant_root/f'memory-{repeat}.json').write_text(json.dumps(samples),encoding='utf-8')
                reports.append(report);(run/'report.json').write_text(json.dumps(reports,indent=2),encoding='utf-8')
                print(json.dumps(report),flush=True)
                if error:break
        finally:
            shutdown='already exited'
            if worker.poll() is None:
                try:
                    api('forja/benchmark/shutdown',{})
                    shutdown='authenticated graceful shutdown'
                    worker.wait(timeout=10)
                except Exception:
                    shutdown='graceful shutdown unavailable; terminated owned worker'
                    worker.terminate()
                    try:worker.wait(timeout=10)
                    except subprocess.TimeoutExpired:worker.kill();worker.wait()
            (variant_root/'exit.json').write_text(json.dumps(dict(pid=worker.pid,exit_code=worker.returncode,shutdown=shutdown,exited=worker.poll() is not None)),encoding='utf-8')
    time.sleep(3)
nv.nvmlShutdown()
