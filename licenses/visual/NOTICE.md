# Componentes del modo visual

El modo visual no utiliza OpenCode ni el Qwen 27B de programación. Descarga componentes independientes solo al pulsar Preparar. Conservar los avisos y licencias incluidos en cada distribución; este archivo no reemplaza sus textos completos.

- HiDream-O1-Image Full (perfil activo) y Dev anterior: HiDream.ai, MIT. Texto completo en HiDream-MIT.txt. Fuente: https://github.com/HiDream-ai/HiDream-O1-Image . Checkpoint Full FP8 escalado empaquetado por Comfy: https://huggingface.co/Comfy-Org/HiDream-O1-Image . La conversión FP8 modifica precisión; no es un modelo distinto ni una afirmación de calidad idéntica.
- Asistente opcional Gemma 4 12B INT8 (E4B anterior): Google, Apache 2.0 según la ficha del proveedor. https://huggingface.co/google/gemma-4-12B-it ; empaquetado: https://huggingface.co/Comfy-Org/gemma-4 . El generador HiDream no requiere este asistente para ejecutarse.
- Modelos FLUX.2 Klein 4B, Qwen3 4B y VAE anteriores: Apache 2.0; se conservan en disco y solo se borran desde almacenamiento por acción expresa. https://huggingface.co/Comfy-Org/flux2-klein-4B
- Wan 2.2 A14B I2V NVFP4 (expertos high/low), destilación de LightX2V: Apache 2.0. https://huggingface.co/lightx2v/LightWan2.2-A14B . VAE Wan 2.1: https://huggingface.co/Comfy-Org/Wan-2.2 . Sol utiliza el operador compilado de Comfy Kitchen incluido en el motor; es opcional, con top-k 10%, sin el runtime externo LightX2V.
- Perfil RTX 3090: GGUF Q4_K_S de QuantStack (https://huggingface.co/QuantStack/Wan2.2-I2V-A14B-GGUF), LoRA LightX2V de Comfy-Org/Wan-2.2 y cargador ComfyUI-GGUF de City96 (Apache 2.0, revisión 6ea2651e7df66d7585f6ffee804b20e92fb38b8a). La biblioteca Python gguf 0.17.1 conserva sus avisos en la dependencia aislada del cargador.
- Wan 2.2 TI2V-5B y VAE anteriores (retirados del perfil activo): Wan AI, Apache 2.0. https://huggingface.co/Wan-AI/Wan2.2-TI2V-5B
- Codificador UMT5 XXL: Google, Apache 2.0; conversión FP8 para ComfyUI. https://huggingface.co/google/umt5-xxl y https://huggingface.co/Comfy-Org/Wan_2.2_ComfyUI_Repackaged
- ComfyUI 0.42.1: GPL-3.0. El motor se descarga como paquete oficial separado, no se incorpora al ejecutable de Forja. Código fuente correspondiente: https://github.com/Comfy-Org/ComfyUI/tree/v0.42.1 y https://github.com/Comfy-Org/ComfyUI/releases/tag/v0.42.1
- SharpCompress 0.50.7: copyright Adam Hathcock, MIT. Fuente exacta: https://github.com/adamhathcock/sharpcompress/tree/b4af8afe6683d019d504954a3b4e4f18b06e2cd6

Python, PyTorch/CUDA, NumPy, Pillow, PyAV/FFmpeg y demás dependencias del portable conservan sus licencias y avisos en el paquete oficial. No se descargan ni usan componentes de Comfy Cloud para este modo. Exportación GIF con Pillow y MP4 con PyAV del motor; no se instala un FFmpeg externo.

Archivos, tamaños, revisiones y SHA-256 fijados en `src/MiIALocal.Core/VisualModels.cs`. Los flujos de Forja se basan en los ejemplos oficiales de ComfyUI: https://github.com/Comfy-Org/workflow_templates y https://docs.comfy.org/tutorials/video/wan/wan2_2 . El nodo de autenticación y exportación GIF de Forja está en `src/MiIALocal.Core/VisualAssets/forja_guard.py`.
