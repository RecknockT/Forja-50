# Perfil visual unificado · Forja 50 0.4.19

Desde el 26/10/2026 el único modelo activo para crear y editar es **HiDream-O1 Full FP8 escalado** (8,07 GB), en RTX 5080, 5070 Ti y 3090. El único asistente disponible es **Gemma 4 12B INT8** (12,06 GB); se puede desactivar, pero no elegir otro modelo. Se cargan por turnos, no simultáneamente en VRAM.

Crear y editar usan la receta del ejemplo de Comfy: 40 pasos, CFG 5, `dpmpp_2m_sde_gpu`, escala de ruido 8 y suavizado de costuras. La descarga conserva tamaño, revisión y SHA-256 fijados. Dev FP8/BF16 y Gemma E4B quedan en Modelos anteriores y se borran solo por selección expresa. Los ajustes antiguos ya no los pueden activar ni duplicar la descarga de Full.

Se comprobaron catálogo, migración, grafos de creación/edición, descarga/preparación y ventanas sin cargar modelos. La inferencia del flujo unificado queda pendiente; no se prometen tiempos, consumo ni una mejora medida de calidad. Wan conserva sus perfiles de animación independientes.

Fuente de la receta: https://github.com/Comfy-Org/workflow_templates/blob/main/templates/image_hidream_o1.json

---

## Investigación histórica de la primera migración

### Migración inicial a HiDream-O1 Dev

Investigación y plan del 21/10/2026. Estado: planificación; ningún peso de HiDream descargado ni inferencia realizada por esta investigación. No representa una migración terminada ni una mejora medida.

## Decisión propuesta

Usar **HiDream-O1-Image-Dev** mediante la integración nativa de ComfyUI como primera implementación. No confundir O1 con el anterior HiDream-I1-Dev, que utiliza otra arquitectura y otros componentes.

Para RTX 5080 y 5070 Ti, empezar por `hidream_o1_image_dev_fp8_scaled.safetensors` de Comfy-Org: descarga pública, versión reducida mantenida por el integrador del motor y flujo oficial de generación/edición. Dev-2604 es el segundo candidato obligatorio de comparación, no un reemplazo silencioso del Dev original. Su ficha lo orienta a texto a imagen; comprobar por separado sus ediciones y el formato compatible antes de elegirlo como valor predeterminado.

La mejor versión en nuestra PC todavía debe determinarse con imágenes comparables. No hay evidencia suficiente para afirmar que Dev-2604, MXFP8 o el modelo completo sean mejores en todas las tareas.

Fuentes: [modelo del autor](https://huggingface.co/HiDream-ai/HiDream-O1-Image-Dev), [Dev-2604](https://huggingface.co/HiDream-ai/HiDream-O1-Image-Dev-2604), [paquete Comfy](https://huggingface.co/Comfy-Org/HiDream-O1-Image), [guía nativa](https://github.com/Comfy-Org/docs/blob/main/tutorials/image/hidream/hidream-o1.mdx).

## Versiones comparadas

| Variante | Uso en la evaluación | Motivo o límite |
|---|---|---|
| O1 Dev original FP8 escalado de Comfy | Primera implementación en 16 GB | Integración nativa, 28 pasos, pesos de 8,07 GB. |
| O1 Dev original BF16 de Comfy | Candidato inicial 3090; referencia de precisión | Pesos de 16,37 GB; el pico total y la coexistencia con Windows deben medirse. |
| O1 Dev original MXFP8 | Experimento posterior en Blackwell | No asumir una ganancia sobre FP8 escalado ni usarlo en la 3090. |
| O1 Dev-2604 | Comparación de calidad, generación y edición | Pesos oficiales originales suman aproximadamente 35,2 GB en F32; no descargarlos como paquete por defecto para equipos con 32 GB de RAM. Una conversión comunitaria FP8 ocupa 8,81 GB, pero usa un formato/cargador diferente. |
| O1 Full | Comparación puntual de edición si Dev falla | El autor lo recomienda para edición; 50 pasos. No añadir otra descarga automáticamente ni afirmar que siempre supera al destilado. |
| O1 en FP16 forzado | Excluir del perfil normal | El código de Comfy advierte desbordamientos en activaciones y NaN; cálculo BF16/F32 con la política específica del modelo. |

El refinador oficial `HiDream-ai/Prompt-Refine` es de 31B. No forma parte del paquete básico: sería una carga importante en 16 GB/32 GB y no es un requisito para ejecutar el generador. La expansión de pedidos se evalúa como etapa independiente, con un asistente más pequeño y liberación de memoria entre etapas.

Fuentes: [código del motor](https://github.com/Comfy-Org/ComfyUI/blob/master/comfy/supported_models.py), [archivos Comfy](https://huggingface.co/Comfy-Org/HiDream-O1-Image/tree/main/checkpoints), [conversión Dev-2604 del autor de la conversión](https://huggingface.co/drbaph/HiDream-O1-Image-Dev-2604-FP8), [refinador](https://huggingface.co/HiDream-ai/Prompt-Refine).

## Configuración inicial y selección por GPU

Perfiles candidatos, pendientes de prueba física. La capacidad total no equivale a la memoria libre y el tamaño del archivo no equivale al pico de VRAM.

| Ajuste | RTX 5080, 16 GB | RTX 5070 Ti, 16 GB | RTX 3090, 24 GB |
|---|---|---|---|
| Pesos iniciales | Dev FP8 escalado | Dev FP8 escalado | Dev BF16 si supera la prueba de memoria; FP8 escalado con conversión compatible como alternativa explícita |
| Cálculo | Política BF16 del modelo; operaciones FP8 selectivas del motor | Igual, sin copiar optimizaciones exclusivas de otra arquitectura | BF16; sin exigir kernels FP8/MXFP8 de Blackwell |
| Resolución de prueba inicial | 1024 cuadrada | 1024 cuadrada | 1024 cuadrada |
| Escalera de calidad | 1536 y 2048 después de validar memoria | 1536 y 2048 después de validar memoria y tiempo | 1536 y 2048 después de validar memoria |
| Pasos | 28 | 28 | 28 |
| Lote / trabajos simultáneos | 1 / 1 | 1 / 1 | 1 / 1 |
| Referencias iniciales | Una; después dos | Una; después dos | Una; después dos |
| Reserva GPU inicial | 1,5 GiB, reajustar según medición | 1,5 GiB, reajustar según medición | 2 GiB, reajustar según medición |
| Asistente | Por etapas; descargar antes de generar | Por etapas; descargar antes de generar | Por etapas; evitar coexistencia innecesaria |

No bajar a 20 pasos ni subir a 50 como arreglo universal: primero reproducir el flujo de 28 pasos. En Comfy, CFG 1 equivale a no añadir guía de clasificador; el script del autor expresa esa ausencia como guidance 0. No mezclar convenciones entre implementaciones.

Punto de partida del flujo oficial Comfy consultado: `SamplerCustom`, `SamplerLCM` (parámetros 1, 1, 2.5), `BasicScheduler normal`, 28 pasos, denoise 1, CFG 1, `ModelNoiseScale 7.6`. Mantener estos parámetros vinculados a la revisión exacta del flujo; no copiar Euler/simple de FLUX ni modificar arbitrariamente el calendario destilado.

Para edición con Dev (una única imagen de referencia, que es el caso de Editar y de una escena nueva con referencia), este programa usa el **programador de edición** que recomienda el autor: Euler de flujo, sin la reinyección de ruido por paso del camino destilado (`SamplerLCM` con `noise_clip_std`), con 28 pasos, CFG 1 y la escala de ruido inicial. La referencia sigue entrando por `HiDreamO1ReferenceImages` y la densidad inicial por `ModelNoiseScale`. El camino destilado con `SamplerLCM` queda solo para crear sin referencia: aplicado a una edición producía ruido. El autor además recomienda el modelo **Full** para editar; no se descarga por defecto. La integración Comfy se verifica con su propio flujo y no se traduce cambiando solamente el nombre del sampler.

Comparar atención estándar de PyTorch y gestión normal de memoria frente a `lowvram` forzado. No conservar por costumbre todas las opciones de FLUX/Wan ni imponer FlashAttention, Triton, torch.compile o kernels Linux en el paquete Windows. Fijar versión de Python, PyTorch/CUDA y Comfy que pase las pruebas de las tres familias. El runtime actual contiene código O1, pero eso no prueba inferencia.

## Descarga e integridad comprobadas

Se consultó la API pública y se comprobó HEAD HTTP 200 del Dev FP8 sin credenciales, sin descargar sus pesos. El repositorio figura `gated: false`, licencia MIT.

Repositorio Comfy-Org/HiDream-O1-Image, revisión `377ec7124bc46a15736c68cd9e4ad6d7da7a7614`:

| Archivo | Bytes | SHA-256 |
|---|---:|---|
| checkpoints/hidream_o1_image_dev_fp8_scaled.safetensors | 8067535296 | 7cbf53a475e0a13f92f2ec08bcffdb9b9de4305ef3b6f35cdd784d09dcd8d0cc |
| checkpoints/hidream_o1_image_dev_bf16.safetensors | 16365209824 | cede93912730dd046fd39a20af008344a7dce75dafabd16e419a222c3e504051 |
| checkpoints/hidream_o1_image_dev_mxfp8.safetensors | 8923300207 | efa55a996f34853df8456e06b54fb6cba802df9d1a07eac5b979db76b58b91e2 |

Dev-2604 oficial, revisión `b6acc2fe452b3120430620dc4354fa442ee081ea`, público/MIT. Conversión drbaph/HiDream-O1-Image-Dev-2604-FP8, revisión `031578e10c7c3980a11b84b4872f87082f7fdb7f`, `model.safetensors`: 8807336008 bytes, SHA-256 `f53658e9204d0dcd9b3fc0f9bb3a1920cbc66126b28a3dadbbfd71b475469e6b`. Ese archivo no se acepta como sustituto intercambiable del checkpoint nativo sin validar conversión y cargador.

El generador O1 lleva su procesamiento de texto y conversión a píxeles integrados; no necesita los archivos VAE y codificador de FLUX. Los nodos llamados CLIP y VAE del checkpoint son adaptadores del motor. Gemma del flujo de ejemplo es un ampliador opcional, no el codificador obligatorio de O1. Si se añade, verificar por separado pesos, licencia, tamaño y compatibilidad; no confundirlo con el refinador 31B ni presentar esos componentes como parte de los 8,07 GB básicos.

## Evidencia externa de rendimiento

[VRAM16](https://vram16.ullu-egg.com/models/hidream-o1-dev/) publica su medición del Dev FP8 escalado en Windows 11, RTX 5080, 64 GB RAM, Comfy 0.38 y PyTorch 2.14 cu130: aproximadamente 4 s a 1024, 9 s a 1536 y 16 s a 2048, con picos publicados de unos 9,5–10,3 GB. Excluye referencias y ampliador. Su metodología declara medición/escritura asistida por IA: usar como indicio reproducible, no como certificación ni promesa de Forja. Nuestra PC tiene 32 GB RAM y ajustes diferentes. No extrapolar esos tiempos a 5070 Ti, 3090, ediciones o primera carga.

## Implementación, en orden

1. **Preservar y separar el trabajo actual.** Hay cambios locales no publicados de FLUX 3 y mejoras de referencias. Conservarlos; retirar FLUX 3 de la ruta predeterminada, sus requisitos de token y su obligatoriedad de asistente/licencia. Mantener las mejoras genéricas de selección previa, historial y errores. No hacer un reset general.
2. **Catálogo por modelo y perfil.** Añadir checkpoint HiDream con revisión, tamaño, hash y licencia; incluir `checkpoints` en el mapa del motor. Separar activos necesarios para imágenes de los de animación y los heredados. Nunca descargar BF16, FP8 y MXFP8 juntos por defecto. Conservar la gestión explícita de archivos anteriores y de importaciones externas.
3. **Descargador y preparación.** Reutilizar 16 trabajadores, fragmentos persistentes de 64 MiB, pausa/reanudación, velocidad reciente y verificación. Mostrar bytes pendientes y espacio adicional; descargar después de instalar y solo por acción del usuario. Sin cuentas, tokens ni instalación de dependencias por consola. Runtime preempaquetado y verificable; sin pip/git pull durante el inicio.
4. **Generación y edición nativas.** Sustituir el grafo FLUX por el grafo O1 fijado y validado; cancelar, reportar progreso y tiempos; errores de carga o agotamiento de memoria deben dejar la interfaz operativa. Registrar modelo, precisión, revisión de flujo, dimensiones, semilla, pasos, pedido original y preparado en el historial.
5. **Pedidos y referencias.** Usar descripciones claras y fieles al pedido. Una referencia aprobada se pasa realmente a O1, no se reemplaza por una descripción textual. Mantener originales y proporciones; mostrar si una imagen es pequeña. Buscar Frieren no equivale a identificarla correctamente: conservar confirmación visual y fuente, y permitir reemplazar la referencia. Un estilo general no debe forzar selección de personaje, pero un nombre que la persona escribió debe conservarse textualmente. El meta-prompt del asistente va en el idioma de la persona y exige la salida en inglés; el título de la referencia elegida se pasa como nombre del sujeto. Asistente local ligero para español/inglés por separado; cargarlo y liberarlo antes de O1 y ofrecer edición del pedido preparado.
6. **Experiencia de uso.** Mantener Iniciar/Detener arriba, acciones Crear/Editar/Animar y estados simples: descargando, preparando pedido, cargando, generando, guardando. Mostrar memoria usada/libre de la placa y aclarar si incluye otros programas. Configuración técnica en avanzado; resoluciones disponibles según perfil validado. El botón iniciar descarga únicamente lo necesario para la acción elegida, con tamaño visible antes de comenzar.
7. **Animación independiente.** Wan sigue creando MP4/GIF; O1 no es un modelo de vídeo. Descargar y cargar Wan solo al pedir animación. Liberar HiDream antes de Wan; ningún motor de programación, O1 o Wan debe competir innecesariamente por la GPU. Si se genera una imagen para animarla, ejecutar etapas sucesivas.
8. **Compatibilidad y almacenamiento.** Historial anterior legible; resultados y originales intactos; nuevos campos con valores migrables. No borrar FLUX ni sus parciales como efecto de cambiar modelo. Ofrecer limpieza de componentes antiguos solo desde almacenamiento con la acción explícita habitual. Confirmación de cierre y parada de procesos siguen funcionando.
9. **Prueba y publicación.** Solo establecer valor predeterminado tras cumplir los criterios de abajo; actualizar licencias y documentación, Setup y portable, manifiesto y actualización. No publicar el trabajo intermedio de FLUX 3 como si fuera esta migración. No afirmar validación de 5070 Ti/3090 a partir de la 5080.

Archivos principales afectados: `VisualModels.cs`, `VisualProvisioner.cs`, `VisualWorkflows.cs`, `VisualRuntime.cs`, `VisualPane.cs`, `VisualAssets/forja_guard.py`, `StorageCleanup.cs`, pruebas visuales, empaquetado, licencias y documentación. No se cambia el backend de programación por migrar el de imágenes.

## Pruebas y criterio de elección final

- Primero: contrato del grafo contra el runtime fijado, sin pesos; rechazo de hashes erróneos, descargas interrumpidas, espacio insuficiente, pausa y reanudación; migración de historial y limpieza respetando archivos externos.
- Después: prueba física en la 5080 con 32 GB RAM. Un primer arranque y tres generaciones siguientes por configuración; registrar tiempo de carga, preparación y generación por separado, pico VRAM total/proceso si disponible, pico RAM y errores. Mantener semilla y pedido controlados; una semilla igual no implica imágenes idénticas entre variantes.
- Conjunto de calidad: anime de los 90 sin personaje obligatorio; Frieren con referencia aprobada; fotografía; texto en español; logo. Editar color de una prenda, quitar un elemento y cambiar pose. Revisar cara, vestuario, accesorios, texto, zonas no solicitadas y costuras/cuadrículas. Conservar entradas y todos los resultados, no elegir solo el mejor ejemplo.
- Comparación A/B: Dev FP8 frente a BF16, Dev-2604 y FLUX.2 de referencia, por fases para evitar descargar/cargar todo a la vez. MXFP8 y Full solo si resuelven una cuestión concreta. Priorizar fidelidad y estabilidad sobre una pequeña diferencia de tiempo.
- Prueba de ciclo de vida: cambiar programación/imagen/animación; cerrar con trabajo activo, confirmar/cancelar; detener y reiniciar; GPU ocupada por otro programa; caída del worker; cancelar preparación sin dejar GPU residente.
- Verificar Setup y portable, primera descarga sin sesión HF, exportación PNG, animación GIF/MP4 y actualización sin perder datos. No declarar terminada la integración si solo pasa la generación desde texto.
- Si Dev-2604 necesita componentes incompatibles o empeora edición, conservar Dev original e informar la diferencia. Si edición Dev no cumple, comparar Full y decidir con resultados antes de imponer un segundo modelo.

Entrega prevista: HiDream Dev operativo para creación y edición en Forja, descarga sin cuenta, perfiles verificables por GPU y prueba en 5080. Fecha y tiempos finales de generación dependen de la implementación y las mediciones; no estimarlos multiplicando pasos por los de FLUX.
