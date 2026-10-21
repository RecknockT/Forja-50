# Plan: programación y creación visual en Forja

Fecha: 21 de octubre de 2026. Estado: implementación inicial 0.3.0; pruebas de inferencia en GPU pendientes.

## Objetivo y alcance

Dos modos elegibles antes de iniciar: **Programación** y **Imágenes y animación**. El segundo incluye creación de imágenes, edición con referencias y animación corta exportable como MP4 o GIF. Windows 11 x64, RTX 5070 Ti / RTX 5080 de escritorio (16 GB) y RTX 3090 (24 GB), con 32 GB de RAM. No requiere servicios de IA en la nube ni una cuenta para generar; las descargas iniciales sí requieren conexión.

Este cambio añade capacidades a la misma aplicación. Se conservan el instalador, portable, actualizador, administración de almacenamiento, sesiones existentes y configuración de programación. El desarrollo no instala Forja ni descarga pesos en el equipo del usuario.

## Experiencia de uso

1. Al abrir, elegir entre dos tarjetas: Programación e Imágenes y animación. En actualizaciones, Programación continúa como modo inicial para conservar el comportamiento existente.
2. Mostrar placa detectada, modo seleccionado, modelos, configuración efectiva, tamaño pendiente y espacio necesario.
3. Preparar el modo descarga exclusivamente sus componentes, con licencias, integridad, pausa y reanudación. La animación es un paquete opcional separado para evitar descargarlo al querer solo imágenes.
4. Iniciar IA abre el espacio del modo elegido. No inicia automáticamente al cambiar una selección.
5. Programación conserva proyecto, Git y chat actuales. Creación visual ofrece chat, adjuntar referencia, ajustes sencillos y galería con Abrir carpeta, Guardar como y Exportar.
6. Creación no exige repositorio Git: usa una carpeta de creaciones elegible, sin acceso predeterminado a proyectos o credenciales de GitHub.
7. Cambiar de modo con la IA activa ofrece detener el modo actual; si existe una generación, permite esperar o cancelarla. Cancelar la decisión conserva la sesión actual. Detener no borra resultados ni descargas.
8. Al cerrar se mantiene la confirmación existente y se detienen todos los procesos propios. Las generaciones interrumpidas se marcan como tales; no se promete reanudar el cálculo a mitad de una difusión.

La ventana mantiene el tema oscuro. Configuración avanzada separada por modo; el resumen visible muestra modelos, precisión, resolución y perfil efectivos. Los parámetros de contexto de programación no se reutilizan como parámetros visuales.

## Modelos y plataforma propuestos

- Imágenes iniciales: **FLUX.2 Klein 4B**, selección documental preferida por generación y edición con referencias en un único modelo. **Z-Image-Turbo** queda como competidor para rapidez y realismo. Confirmar el predeterminado con una comparación local antes de fijar el catálogo; inicialmente descargar uno, no ambos obligatoriamente. Ver [comparación de candidatos](SELECCION-MODELOS-VISUALES-2026-10-21.md).
- Animación opcional: **Wan 2.2 TI2V-5B**, mediante el flujo de ComfyUI con transferencia de componentes a RAM. La cifra de 8 GB documentada por ComfyUI corresponde a ese flujo optimizado; no es una garantía del script original ni de cualquier resolución o duración.
- Motor visual: **ComfyUI local administrado**, con versión y flujos fijados. No instalar extensiones arbitrarias ni usar Comfy Cloud para este modo. Mantener su interfaz de nodos fuera del recorrido habitual.
- Exportación: MP4 mediante PyAV del motor y GIF con Pillow; guardar ambos resultados. GIF reduce colores, no conserva audio y no garantiza un bucle continuo automáticamente.
- OpenCode y Qwen se usan exclusivamente en Programación; la interfaz visual envía descripciones y referencias directamente a ComfyUI.

Las versiones, archivos, codificadores, VAE, tamaños, hashes, licencias y dependencias Windows deben registrarse juntos antes de habilitar cada paquete. No basta con descargar el archivo principal del modelo. La licencia del motor no sustituye la de los pesos o FFmpeg.

## Modos independientes: diseño definitivo

La decisión posterior del usuario reemplaza la coordinación OpenCode/MCP propuesta inicialmente. Programación conserva Qwen y OpenCode. Imágenes y animación usa una interfaz WPF propia, ComfyUI y sus modelos: no requiere el agente, su asistente de texto ni un repositorio Git.

Forja envía la descripción directamente al flujo. Para editar envía además la imagen seleccionada mediante ReferenceLatent. Tras una imagen completada se selecciona ese resultado como referencia y se pasa a Editar; Imagen nueva quita la referencia. No se promete memoria conversacional o inspección visual por un LLM: el contexto de edición es la imagen elegida y la instrucción actual.

Cada modo mantiene sus ajustes y preparación. Solo se permite uno activo: cambiar exige detener el anterior; no se detiene nada al rechazar la confirmación. Cerrar confirmado cancela la operación activa y libera ambos supervisores de procesos propios. Una generación cancelada se registra y puede repetirse; sus cálculos no se reanudan desde un paso intermedio.

El motor visual se conecta por loopback con credencial efímera, rechazo de orígenes de navegador y nodos externos deshabilitados salvo el puente propio. Solo se construyen flujos conocidos. No se heredan sesión GitHub, permisos de edición o shell del modo de programación. Las referencias se copian y los originales permanecen intactos.

Exportación: el flujo produce MP4 con PyAV del motor y GIF mediante un nodo propio basado en Pillow. No se añade un FFmpeg externo. La primera versión ofrece vista previa estática y apertura con el visor del sistema para reproducir animaciones.

## Perfiles por placa

| Placa | Política inicial propuesta |
| --- | --- |
| 5070 Ti, 16 GB | Una generación por vez, transferencia a RAM y límites conservadores. |
| 5080, 16 GB | Mismo límite de memoria; aceleraciones compatibles con RTX 50 verificadas por separado. |
| 3090, 24 GB | Más residencia de componentes en GPU si cabe; ruta compatible con Ampere, sin asumir FP8/FP4 nativos. |

Probar imágenes de alrededor de 1024 px y clips de 3–5 segundos a resolución moderada como objetivos iniciales, no como prestaciones garantizadas. Ajustar a las dimensiones/fotogramas admitidos por cada flujo. La 3090 no recibe automáticamente más resolución si no se ha medido el consumo. Ante memoria insuficiente, explicar el ajuste posible; no reducir calidad o cambiar modelo silenciosamente.

## Persistencia, descargas y almacenamiento

- Añadir `AppMode` y configuración visual independiente, con migración que conserve todos los campos y valores actuales de programación. No renombrar carpetas ni trasladar los modelos existentes como parte de esta función.
- Directorios separados para runtime visual, modelos por componente, entradas copiadas, trabajos y resultados. Salidas y referencias del usuario se protegen de la limpieza de modelos/caché.
- Reutilizar el descargador verificado de 16 trabajadores y fragmentos persistentes. Mostrar tamaño agregado del paquete, uso temporal y espacio de extracción; verificar hash antes de activar.
- Trabajos durables con identificador, parámetros, versión del flujo, semilla efectiva, archivos y estado. Al reiniciar, reconciliar trabajos interrumpidos; no relanzarlos ni sobrescribir resultados automáticamente.
- Cancelar una generación elimina solo temporales propios; la reanudación de descargas es distinta de reiniciar una generación. Un OOM o fallo del trabajador no debe cerrar Forja ni corromper el chat.
- Las actualizaciones conservan componentes fijados y trabajos previos hasta completar una migración compatible. La galería debe poder abrir resultados existentes sin iniciar modelos.

## Secuencia de implementación y criterios de salida

1. **Prueba de arquitectura:** modos separados y protocolo ComfyUI simulado. Verificar referencia, resultados, cancelación y cierre sin descargar modelos ni inferir.
2. **Selector de modos:** ajustes independientes, preparación y estados por modo, sin regresiones de Programación. Crear las vistas y galería vacía, sin presentar capacidades simuladas como reales.
3. **Paquete visual reproducible:** runtime Windows y dependencias fijados; verificar compatibilidad RTX 30/50, integridad, descarga interrumpida, falta de disco y licencias. No compilar CUDA durante la instalación normal del usuario.
4. **Imágenes de punta a punta:** texto a imagen y edición con referencia, progreso, cancelación, exportación y recuperación. Comparar FLUX/Z-Image y fijar el modelo inicial con resultados reproducibles.
5. **Animación opcional:** Wan, límites probados, MP4 y GIF, errores y espacio de salida. Medir tiempos en vez de prometer velocidad.
6. **Validación y entrega:** pruebas en cada GPU física o informe explícito de las pendientes; Windows limpio, Setup/portable, actualización desde la versión actual y limpieza sin tocar proyectos/resultados. Publicar solo cuando pasen las comprobaciones pertinentes, indicando las capacidades aún experimentales.

Se pueden automatizar los contratos con procesos y respuestas simuladas, rutas con espacios/Unicode, validación de parámetros, descargas y aislamiento de permisos. Las simulaciones no sustituyen inferencia física ni mediciones de VRAM/RAM. Las pruebas que carguen modelos se coordinan con el usuario para no interferir con su PC.

## Fuentes verificadas para el diseño

- OpenCode, MCP: https://opencode.ai/docs/mcp-servers/
- ComfyUI, API de trabajos, progreso e interrupción: https://docs.comfy.org/development/comfyui-server/comms_routes
- ComfyUI, Wan 2.2 y flujo 5B con descarga a RAM: https://docs.comfy.org/tutorials/video/wan/wan2_2
- FLUX.2 Klein 4B: https://huggingface.co/black-forest-labs/FLUX.2-klein-4B
- Z-Image: https://github.com/Tongyi-MAI/Z-Image

Los detalles de la API y los modelos se vuelven a contrastar con las versiones exactas fijadas al implementar. Este documento no certifica las tres placas; las comprobaciones de protocolo y UI no sustituyen inferencia física.
