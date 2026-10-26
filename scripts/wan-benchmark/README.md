# Comparación manual de animación en RTX 5080 / 5070 Ti

Herramientas de desarrollo, fuera del instalador. No modifican los ajustes, modelos ni paquetes Python de Forja. El benchmark carga pesos y usa la GPU: cerrar otros trabajos de IA antes de ejecutarlo.

## Preparación

Requiere una instalación de Forja con ComfyUI 0.42.1, Wan 5B, UMT5 FP8 y Wan 2.2 VAE ya preparados. Usar el Python integrado del motor, que incluye Torch, psutil, Pillow y PyAV. Git debe estar disponible para preparar el cargador GGUF.

1. Elegir una carpeta de resultados fuera de Git, por ejemplo `artifacts/wan-benchmark`.
2. Ejecutar `prepare.py CARPETA --embedded-python PYTHON_COMFY` con Python. Recupera metadatos de revisiones fijas y prepara GGUF en esa carpeta; no instala paquetes globales.
3. Ejecutar `download.py CARPETA`. Descarga siete archivos, unos 37 GB, con 16 trabajadores, fragmentos persistentes de 64 MiB e integridad SHA-256. No se descargan otra vez los componentes compartidos ya instalados.
4. Copiar una imagen de referencia a `CARPETA/reference.png`, preferentemente con las mismas proporciones que el video.
5. Ejecutar con Python Comfy: `run.py CARPETA DATOS_FORJA --allow-gpu --variants nvfp4,gguf,5b --repeats 3 --width 576 --height 576 --disable-pinned-memory`.

`DATOS_FORJA` es la carpeta de datos existente, no la del ejecutable. Se puede indicar otra referencia con `--reference ARCHIVO`.

## Qué se mide

- Un proceso de motor nuevo por variante y tres pedidos idénticos por proceso. Mismo texto, referencia, semilla, resolución, 81 fotogramas y salida MP4 a 24 FPS (3,375 s).
- Tiempo desde envío del pedido hasta completar el archivo, incluyendo texto, carga, referencia, difusión, decodificación y exportación. Inicio del proceso registrado por separado. Primer pedido separado de los siguientes; no equivale a vaciar la caché de disco de Windows.
- Memoria total GPU mediante NVML y memoria residente/privada del proceso mediante psutil, cada 200 ms. La memoria privada es compromiso de memoria y puede incluir paginación: no confundirla con RAM residente. La VRAM total incluye escritorio y otras aplicaciones.
- Carga y muestreo separados por experto. Se exige `force_full_load=True` también en la preparación interna del sampler y se comprueba tamaño cargado al terminar. Si no cabe, registrar el fallo; no interpretarlo como una ejecución residente exitosa.
- Se cuentan las llamadas de multiplicación NVFP4. No son un perfil exhaustivo de kernels ni prueban por sí solas ausencia de paginación implícita de Windows.
- NVFP4 utiliza pesos LightWan convertidos para ComfyUI, sin LoRA adicional. GGUF Q4_K_S utiliza las dos LoRA LightX2V de la plantilla oficial. Wan 5B usa 20 pasos, CFG 5 y uni_pc/simple.

## Límites de la comparación

La atención es **PyTorch SDPA densa** en las tres variantes. Esta prueba **no reproduce** el runtime LightX2V con atención dispersa/SageAttention ni sus benchmarks de RTX 5090. No atribuir esos tiempos publicados al resultado de este ensayo.

Las repeticiones con la misma semilla sirven para comparar tiempos; no son escenas independientes para establecer calidad general. Revisar videos completos: cara, manos, vestimenta, fondo, movimiento solicitado, parpadeo y deformaciones. No reducir calidad a una cifra de diferencia entre fotogramas.

Los procesos de prueba se cierran en `finally`. Una interrupción forzada del proceso padre puede saltarse esa limpieza: comprobar los procesos propios antes de iniciar otro ensayo. No ejecutar dos lotes GPU a la vez.

## Atención dispersa experimental

La variante adicional `--variants nvfp4-sol` usa los mismos pesos NVFP4 y cambia únicamente la autoatención de los 40 bloques a los operadores Sol CUDA ya instalados en Comfy Kitchen, con top-k 10% y `tail=False`. Conserva atención cruzada y VAE. Registra el número de llamadas Sol y NVFP4. No requiere instalar SageAttention ni Triton.

Esta ruta es un experimento del benchmark, **no** la implementación DynamicSparse/Sage2 de LightX2V. Tiene una aproximación y una selección por bloques propias; revisar la calidad y no inferir equivalencia numérica con LightX2V. El motor debe ofrecer `sol_attn_is_available` y los bloques deben soportar `ComfyAttention`; si no, la prueba falla explícitamente.

El proceso del motor tiene PID y orden de arranque registrados en `process.json`. Al terminar se solicita su cierre mediante un endpoint autenticado que entrega SIGINT al propio intérprete. Se espera su salida y se registra `exit.json`; solo si ese cierre falla se termina el proceso propio como alternativa. El endpoint existe únicamente en los nodos de desarrollo del benchmark.

`summarize.py CARPETA_DE_EJECUCION` verifica los MP4 con PyAV y genera CSV, resumen JSON, HTML y video comparativo. Para comparar lotes distintos, reunir primero sus registros y archivos respetando los nombres de variante, y dejar constancia del origen y la diferencia de condiciones de base.

## Fuentes

- [LightWan, pesos y receta](https://huggingface.co/lightx2v/LightWan2.2-A14B)
- [GGUF](https://huggingface.co/QuantStack/Wan2.2-I2V-A14B-GGUF)
- [Cargador GGUF](https://github.com/city96/ComfyUI-GGUF)
- [Plantilla oficial I2V 14B](https://github.com/Comfy-Org/workflow_templates/blob/main/templates/video_wan2_2_14B_i2v.json)

No incluir modelos, entradas del usuario ni resultados privados en commits.
