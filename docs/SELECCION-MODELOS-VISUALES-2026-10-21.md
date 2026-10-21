# Selección de modelos visuales para Forja

Revisión documental: 21 de octubre de 2026. Sin descargas de pesos, instalación ni pruebas de inferencia. La recomendación considera Windows, RTX 5070 Ti/5080 de 16 GB, RTX 3090 de 24 GB y 32 GB de RAM. No es una clasificación absoluta de calidad visual.

## Decisión propuesta

**FLUX.2 Klein 4B para generar y editar imágenes; Wan 2.2 TI2V-5B para animaciones, convertidas localmente a GIF o MP4.** ComfyUI administrado ejecutaría ambos mediante una interfaz propia de Forja, independiente de OpenCode/Qwen. Una generación por vez; detener Programación antes de iniciar el modo visual.

La selección prioriza un paquete accesible y reproducible en las tres placas. La cantidad de parámetros no determina por sí sola la calidad, y que un modelo quepa no garantiza una espera aceptable.

## Comparación

| Modelo | Evidencia revisada | Decisión para Forja |
| --- | --- | --- |
| FLUX.2 Klein 4B | Generación y edición con múltiples referencias, cuatro pasos en el ejemplo oficial, Apache 2.0. La ficha declara aproximadamente 13 GB de VRAM y menciona RTX 3090. | Preferido para el paquete inicial de imágenes por sus funciones integradas y presupuesto de memoria. |
| Z-Image-Turbo | 6B, generación rápida y enfoque fotográfico; el proyecto declara compatibilidad con 16 GB. Apache 2.0. Las variantes oficiales de edición/Omni aún figuran como pendientes de publicar en la tabla revisada. | Comparador principal en fotografía; no asumir que Turbo edita referencias como Klein. |
| Qwen-Image 2.1 Turbo | Generación, edición y transparencia; ocho pasos. La arquitectura incorpora un generador de 7B y un codificador Qwen3-VL de 8B: no calcular el consumo como si todo fueran 7B. Licencia Qwen Research de uso no comercial salvo autorización adicional. | No predeterminado para una aplicación de uso general; memoria y condiciones requieren un perfil/evaluación aparte. No confundir esta licencia con la del Qwen de programación. |
| Krea 2 Turbo | Acceso a pesos condicionado a cuenta y aceptación de su licencia comunitaria. | Alternativa para investigar si mejora claramente las pruebas; añade pasos de acceso que el paquete inicial quiere evitar. |
| Wan 2.2 TI2V-5B | Texto e imagen a video. Flujo ComfyUI documentado con descarga de componentes a RAM para 8 GB; pesos Apache 2.0. | Preferido para animaciones cortas en el presupuesto común de 16 GB. La memoria final depende de resolución/fotogramas y del flujo completo. |
| Wan 2.2 I2V-A14B | Modelo especializado de imagen a video, más pesado y con flujos ComfyUI existentes. | Comparador de calidad posterior con cuantización/transferencia a RAM; no prometerlo en 16 GB y 32 GB de RAM sin medir. |
| LTX 2.5 | Alternativa actual de video/audio. LTX Desktop recomienda 24 GB de VRAM y 32 GB de RAM; admite máquinas inferiores con restricciones y no las recomienda. Algunos pesos requieren aceptación en Hugging Face. | No usar como perfil universal inicial de 16 GB. El mínimo del lanzador no demuestra calidad o velocidad adecuadas. |

WanGP también documenta instalación Windows y rutas RTX 30–50, pero añadir una segunda plataforma visual desde el principio amplía dependencias y mantenimiento. Mantener ComfyUI como primera integración; reevaluar WanGP si las mediciones muestran una mejora necesaria.

## Qué debe comprobar la prueba comparativa

- Mismos pedidos y dimensiones: fotografía, ilustración, producto, personaje, composición y texto en español. Varias semillas; no elegir solo el mejor ejemplo de cada modelo.
- Edición: conservar sujeto y composición al cambiar fondo, color o un objeto. Separar una edición condicionada de regenerar una imagen parecida.
- Animación: estabilidad de identidad, movimiento solicitado, parpadeo y transición entre primer/último fotograma. Repetición de un GIF no implica continuidad del movimiento.
- Medir arranque frío, generación posterior, cambio Qwen/visual, máximo de VRAM y RAM, tamaño total de descarga y fallos. Probar el flujo completo y sus codificadores/VAE, no solo los pesos principales.
- Comprobar rutas de precisión por arquitectura: no exigir aceleraciones FP8/FP4 nativas a la 3090. Medir también Windows con memoria de escritorio ocupada y 32 GB de RAM.
- Validar cada placa por separado. El presupuesto nominal de 16 GB hace viable la candidatura, no certifica la implementación concreta en las dos RTX 50.

Objetivos iniciales para evaluar: imágenes cercanas a 1024 px y animaciones de 3–5 segundos a resolución moderada. No son límites publicados ni tiempos garantizados. GIF requiere conversión, pierde colores/audio y suele pesar más que MP4. No atribuir transparencia animada al flujo Wan por disponer de un exportador GIF.

## Fuentes primarias

- FLUX.2 Klein 4B, funciones y memoria: https://huggingface.co/black-forest-labs/FLUX.2-klein-4B
- Familia FLUX.2, variantes y licencias distintas 4B/9B: https://github.com/black-forest-labs/flux2
- Z-Image, variantes y disponibilidad: https://github.com/Tongyi-MAI/Z-Image
- Z-Image-Turbo, licencia: https://huggingface.co/Tongyi-MAI/Z-Image-Turbo
- Qwen-Image 2.1, arquitectura y optimización de memoria: https://github.com/QwenLM/Qwen-Image-2.1
- Qwen-Image 2.1 Turbo: https://huggingface.co/Qwen/Qwen-Image-2.1-Turbo
- Licencia específica Qwen Research: https://huggingface.co/Qwen/Qwen-Image-2.1-Turbo/raw/main/LICENSE
- Krea 2 Turbo, condiciones de acceso: https://huggingface.co/krea/Krea-2-Turbo
- ComfyUI, flujos Wan 2.2: https://docs.comfy.org/tutorials/video/wan/wan2_2
- Wan 2.2 TI2V-5B, licencia: https://huggingface.co/Wan-AI/Wan2.2-TI2V-5B
- LTX Desktop, requisitos actuales: https://github.com/Lightricks/LTX-Desktop
- WanGP, Windows y generaciones de GPU: https://github.com/deepbeepmeep/WanGP

Antes de implementar se fijarán revisiones y archivos exactos con tamaño/hash, licencias y condiciones de los componentes auxiliares. No se presenta la selección documental como una prueba local o una garantía de rendimiento.
