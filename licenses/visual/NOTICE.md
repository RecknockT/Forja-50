# Componentes del modo visual

El modo visual no utiliza OpenCode ni el Qwen 27B de programación. Descarga componentes independientes solo al pulsar Preparar. Conservar los avisos y licencias incluidos en cada distribución; este archivo no reemplaza sus textos completos.

- FLUX.2 Klein **4B** (no 9B): Black Forest Labs, Apache 2.0. https://huggingface.co/black-forest-labs/FLUX.2-klein-4B
- Codificador Qwen3 4B: Qwen / Alibaba, Apache 2.0. Es un componente del flujo visual, no el asistente de programación. https://huggingface.co/Qwen/Qwen3-4B
- VAE FLUX.2: Black Forest Labs, Apache 2.0. https://github.com/black-forest-labs/flux2
- Pesos empaquetados para ComfyUI: https://huggingface.co/Comfy-Org/flux2-klein-4B
- Wan 2.2 TI2V-5B y VAE: Wan AI, Apache 2.0. https://huggingface.co/Wan-AI/Wan2.2-TI2V-5B
- Codificador UMT5 XXL: Google, Apache 2.0; conversión FP8 para ComfyUI. https://huggingface.co/google/umt5-xxl y https://huggingface.co/Comfy-Org/Wan_2.2_ComfyUI_Repackaged
- ComfyUI 0.42.1: GPL-3.0. El motor se descarga como paquete oficial separado, no se incorpora al ejecutable de Forja. Código fuente correspondiente: https://github.com/Comfy-Org/ComfyUI/tree/v0.42.1 y https://github.com/Comfy-Org/ComfyUI/releases/tag/v0.42.1
- SharpCompress 0.50.7: copyright Adam Hathcock, MIT. Fuente exacta: https://github.com/adamhathcock/sharpcompress/tree/b4af8afe6683d019d504954a3b4e4f18b06e2cd6

Python, PyTorch/CUDA, NumPy, Pillow, PyAV/FFmpeg y demás dependencias del portable conservan sus licencias y avisos en el paquete oficial. No se descargan ni usan componentes de Comfy Cloud para este modo. Exportación GIF con Pillow y MP4 con PyAV del motor; no se instala un FFmpeg externo.

Archivos, tamaños, revisiones y SHA-256 fijados en `src/MiIALocal.Core/VisualModels.cs`. Los flujos de Forja se basan en los ejemplos oficiales de ComfyUI: https://github.com/Comfy-Org/workflow_templates y https://docs.comfy.org/tutorials/video/wan/wan2_2 . El nodo de autenticación y exportación GIF de Forja está en `src/MiIALocal.Core/VisualAssets/forja_guard.py`.
