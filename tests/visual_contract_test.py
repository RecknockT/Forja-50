"""Validate actual Forja graphs against installed ComfyUI contracts on CPU, without weights or inference.

Usage: embedded Python -s tests/visual_contract_test.py COMFY_ROOT GRAPH_DIRECTORY
Only filename availability is mocked; schemas, dynamic inputs and link types are real.
"""
import asyncio
import importlib.util
import json
import os
from pathlib import Path
import sys
import tempfile
import types

comfy_root, graphs = map(Path, sys.argv[1:])
sys.dont_write_bytecode = True
os.environ["CUDA_VISIBLE_DEVICES"] = ""
sys.path.insert(0, str(comfy_root))
temporary = tempfile.TemporaryDirectory(prefix="Forja-contract-")
sys.argv = [sys.argv[0], "--cpu", "--user-directory", temporary.name]
import comfy.options
comfy.options.enable_args_parsing()
import torch
def forbid_cuda(*args, **kwargs):
    raise RuntimeError("CUDA is forbidden in this CPU contract test")
torch.cuda._lazy_init = forbid_cuda
# This engine's Kitchen capability probe initializes CUDA even with --cpu.
# It is irrelevant to schema validation; disable only that optional GPU probe.
import comfy_kitchen
comfy_kitchen.int8_attention_is_available = lambda *a, **kw: False
import nodes
import server
import execution

async def check():
    from aiohttp import web
    server.PromptServer.instance = types.SimpleNamespace(app=web.Application(), routes=web.RouteTableDef(), node_replace_manager=server.NodeReplaceManager())
    for module in ("nodes_textgen.py", "nodes_hidream_o1.py", "nodes_model_advanced.py", "nodes_custom_sampler.py", "nodes_advanced_samplers.py"):
        assert await nodes.load_custom_node(str(comfy_root / "comfy_extras" / module), module_parent="comfy_extras")
    os.environ["FORJA_VISUAL_TOKEN"] = "a" * 64
    source = Path(__file__).resolve().parents[1] / "src/MiIALocal.Core/VisualAssets/forja_guard.py"
    spec = importlib.util.spec_from_file_location("forja_contract_bridge", source)
    bridge = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(bridge)
    nodes.NODE_CLASS_MAPPINGS.update(bridge.NODE_CLASS_MAPPINGS)
    checked = 0
    for graph_file in graphs.glob("hidream-*-graph.json"):
        graph = json.loads(graph_file.read_text(encoding="utf-8-sig"))
        originals = []
        try:
            for node in graph.values():
                cls = nodes.NODE_CLASS_MAPPINGS[node["class_type"]]
                if node["class_type"] in ("CheckpointLoaderSimple", "CLIPLoader", "VAELoader", "LoadImage", "ForjaAssistantLoader"):
                    schema = cls.INPUT_TYPES()
                    for key, value in node["inputs"].items():
                        descriptor = schema.get("required", {}).get(key)
                        if descriptor and isinstance(descriptor[0], list) and value not in descriptor[0]:
                            descriptor[0].append(value)
                    original = cls.INPUT_TYPES
                    originals.append((cls, original))
                    cls.INPUT_TYPES = classmethod(lambda _, fixed=schema: fixed)
            image = nodes.NODE_CLASS_MAPPINGS["LoadImage"]
            old_validate = image.VALIDATE_INPUTS
            image.VALIDATE_INPUTS = classmethod(lambda *a, **kw: True)
            try:
                valid, error, outputs, node_errors = await execution.validate_prompt("contract", graph, None)
                assert valid, json.dumps({"error": error, "nodes": node_errors}, ensure_ascii=False)
                checked += 1
                print("PASS contrato ComfyUI real:", graph_file.name, "outputs:", outputs)
            finally:
                image.VALIDATE_INPUTS = old_validate
        finally:
            for cls, method in reversed(originals):
                cls.INPUT_TYPES = method
    assert checked >= 5, "No se validaron todos los grafos de HiDream"
    assert not torch.cuda.is_initialized(), "La prueba inicializó CUDA"

try:
    asyncio.run(check())
finally:
    temporary.cleanup()
