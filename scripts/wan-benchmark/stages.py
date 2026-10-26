"""Benchmark-only Comfy nodes. Never distributed inside Forja's runtime."""
import gc
import json
import os
import time
import signal
import torch
import nodes
import comfy.model_management as mm
import comfy_kitchen as kitchen
from aiohttp import web
from server import PromptServer

@PromptServer.instance.routes.post('/forja/benchmark/shutdown')
async def shutdown_benchmark(request):
    # Authenticated by the benchmark's forja_guard middleware.
    PromptServer.instance.loop.call_later(.2, signal.raise_signal, signal.SIGINT)
    return web.json_response({'stopping': True})

_nvfp4_calls = 0
_sol_calls = 0
_nvfp4_original = kitchen.scaled_mm_nvfp4
def counted_nvfp4(*args, **kwargs):
    global _nvfp4_calls
    _nvfp4_calls += 1
    return _nvfp4_original(*args, **kwargs)
kitchen.scaled_mm_nvfp4 = counted_nvfp4

def record(stage, event, **values):
    row = dict(stage=stage, event=event, time=time.time(), **values)
    with open(os.environ["FORJA_BENCH_STAGES"], "a", encoding="utf-8") as file:
        file.write(json.dumps(row) + "\n")

class ForjaBenchmarkStage(nodes.KSamplerAdvanced):
    @classmethod
    def INPUT_TYPES(cls):
        schema = super().INPUT_TYPES()
        schema["required"]["stage"] = ("STRING", {"default": "high"})
        return schema

    FUNCTION = "sample_stage"

    def sample_stage(self, stage, **kwargs):
        model = kwargs["model"]
        topk = float(os.environ.get('FORJA_BENCH_SOL_TOPK', '0'))
        if topk:
            from comfy.ldm.modules.attention import ComfyAttention, attention_pytorch, wrap_attn
            if not kitchen.sol_attn_is_available(mm.get_torch_device()):
                raise RuntimeError('Compiled Sol attention unavailable')
            @wrap_attn
            def sol(q,k,v,heads,mask=None,attn_precision=None,skip_reshape=False,skip_output_reshape=False,**options):
                global _sol_calls
                if mask is not None:
                    return attention_pytorch(q,k,v,heads,mask=mask,attn_precision=attn_precision,skip_reshape=skip_reshape,skip_output_reshape=skip_output_reshape,**options)
                batch=q.shape[0]
                if skip_reshape:q,k,v=(t.transpose(1,2) for t in (q,k,v))
                else:q,k,v=(t.reshape(batch,-1,heads,t.shape[-1]//heads) for t in (q,k,v))
                out=kitchen.sol_attn(q,k,v,topk_ratio=topk,tail=False,scale=options.get('scale'))
                _sol_calls+=1
                return out.transpose(1,2) if skip_output_reshape else out.reshape(batch,-1,heads*out.shape[-1])
            model=model.clone();kwargs['model']=model
            patched=0
            for index,block in enumerate(model.model.diffusion_model.blocks):
                if hasattr(block.self_attn,'comfy_attention'):
                    attention=ComfyAttention();attention.function=sol
                    model.add_object_patch(f'diffusion_model.blocks.{index}.self_attn.comfy_attention',attention)
                    patched+=1
            if not patched:raise RuntimeError('No self-attention modules patched')
            record(stage,'sol_attention',topk_ratio=topk,blocks=patched)
        record(stage, "loading")
        mm.unload_all_models()
        gc.collect()
        mm.soft_empty_cache()
        torch.cuda.reset_peak_memory_stats()
        started = time.perf_counter()
        # Fail with OOM instead of silently streaming layers from host RAM.
        mm.load_models_gpu([model], force_full_load=True)
        torch.cuda.synchronize()
        record(stage, "loaded", seconds=time.perf_counter()-started,
               model_bytes=model.model_size(), loaded_bytes=model.loaded_size(),
               lowvram_patches=model.lowvram_patch_counter(), allocated=torch.cuda.memory_allocated())
        started = time.perf_counter()
        initial_calls = _nvfp4_calls
        initial_sol_calls = _sol_calls
        original_load = mm.load_models_gpu
        def fully_load(*args, **options):
            options['force_full_load'] = True
            return original_load(*args, **options)
        mm.load_models_gpu = fully_load
        try:
            result = super().sample(**kwargs)
            torch.cuda.synchronize()
            record(stage, "sampled", seconds=time.perf_counter()-started,
                   allocated_peak=torch.cuda.max_memory_allocated(), reserved_peak=torch.cuda.max_memory_reserved(),
                   model_bytes=model.model_size(), loaded_bytes=model.loaded_size(),
                   lowvram_patches=model.lowvram_patch_counter(), nvfp4_matmul_calls=_nvfp4_calls-initial_calls, sol_attention_calls=_sol_calls-initial_sol_calls)
            if model.loaded_size() < model.model_size():
                raise RuntimeError('Benchmark rejected: active expert was not fully resident after sampling')
            return result
        except Exception as error:
            record(stage, "error", error=repr(error), allocated_peak=torch.cuda.max_memory_allocated())
            raise
        finally:
            mm.load_models_gpu = original_load

NODE_CLASS_MAPPINGS = {"ForjaBenchmarkStage": ForjaBenchmarkStage}
