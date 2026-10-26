# Comparación local de animación · RTX 5080

26 de octubre de 2026. Ensayo de desarrollo separado de la instalación. No cambia el modelo predeterminado de Forja ni certifica la RTX 5070 Ti.

## Condiciones

- RTX 5080, 16303 MiB declarados por NVIDIA; Windows, 32 GB de RAM, controlador 621.01.
- ComfyUI 0.42.1 existente, PyTorch 2.14.0 + CUDA 13.0. Sin instalar paquetes globales.
- Mismo PNG de referencia, mismo pedido de movimiento, semilla 20261026, resolución 576 × 576, 81 fotogramas y exportación H.264 a 24 FPS (3,375 s). Una escena de personaje anime con efectos mágicos.
- Tres pedidos idénticos por variante. Un proceso nuevo por variante; primer pedido separado de los siguientes. Caché de nodos y memoria fijada desactivadas. No se vació la caché de archivos de Windows.
- Tiempo desde envío del pedido hasta completar el MP4: incluye codificación del texto, carga de pesos, referencia, difusión, decodificación y exportación. Inicio del proceso aparte.
- NVML y psutil muestreados cada 200 ms. VRAM total de la placa, no exclusiva del motor. La RAM de la tabla es el máximo residente del proceso, no su compromiso privado.
- Se fuerza carga completa de cada experto también durante la preparación interna del sampler y se comprueba su tamaño cargado al terminar cada etapa. Los expertos se intercambian entre etapas. No se afirma ausencia absoluta de paginación implícita de Windows.

## Resultados

| Variante | Primer pedido | Mediana pedidos 2–3 | Rango pedidos 2–3 | Solo muestreo, mediana | Pico VRAM total | Pico RAM residente |
|---|---:|---:|---:|---:|---:|---:|
| Wan 2.2 5B FP16, 20 pasos CFG 5 | 65,1 s | 59,9 s | 59,8–60,0 s | 37,5 s | 12,90 GiB | 22,04 GiB |
| Wan 2.2 A14B GGUF Q4_K_S + LoRA LightX2V, 4 pasos | 87,4 s | 87,8 s | 83,6–92,1 s | 57,8 s | 13,37 GiB | 10,85 GiB |
| LightWan A14B NVFP4, SDPA densa, 4 pasos | 59,1 s | 58,1 s | 55,9–60,3 s | 27,9 s | 13,37 GiB | 11,58 GiB |
| LightWan NVFP4 + Sol top-k 10%, 4 pasos, experimental | 44,9 s | 41,3 s | 41,0–41,6 s | 8,8 s | 13,99 GiB | 11,81 GiB |

Las primeras tres variantes se midieron en el mismo lote; Sol en un lote posterior, tras reanudar la tarea. VRAM de base: 1,56 / 1,43 / 1,40 / 1,74 GiB respectivamente. Por eso la diferencia de VRAM total de Sol no se debe atribuir íntegramente al método. La asignación máxima Torch durante muestreo fue 10,66 GiB tanto en NVFP4 densa como con Sol.

Los doce pedidos completaron la generación y los doce MP4 se decodificaron con 81 fotogramas. Los expertos terminaron completos en GPU, sin parches de low-VRAM registrados. NVFP4 ejecutó 1600 llamadas de multiplicación por pedido; Sol añadió 160 llamadas de autoatención por pedido.

Una prueba piloto inicial detectó que precargar un experto completo no era suficiente: la preparación interna de ComfyUI podía descargar parte de los pesos después. Se corrigió el benchmark para forzar la carga completa también en ese punto; esas ejecuciones anteriores se excluyeron de la tabla. También se excluyeron los pilotos concurrentes con descargas y los horizontales con bordes extendidos de una imagen cuadrada.

## Calidad y límites

Revisados fotogramas iniciales, intermedios y finales de los cuatro clips. En esta escena, 5B mantiene la composición pero presenta poco movimiento; GGUF anima más el personaje y cambia más sus proporciones/rasgos. NVFP4 conserva la escena con gestos más discretos; Sol cambia también la evolución de la pose, el rostro y el báculo. Ninguno garantiza edición o movimiento exactos.

No se asigna una puntuación objetiva de calidad: una escena y una semilla no permiten establecer un ganador general. El informe local conserva los videos completos sincronizados y los registros para que el usuario compare por sí mismo. No se publican sus imágenes, videos ni rutas personales en el repositorio.

La ruta Sol fue la más rápida de las probadas y es candidata a evaluación adicional. No se midió 720p ni se validó la 5070 Ti. No extrapolar velocidades publicadas de una RTX 5090: estas pruebas no ejecutan el runtime completo LightX2V con su implementación Sage2 de atención dispersa.

## Reproducibilidad

Herramientas y procedimiento: [scripts/wan-benchmark](../scripts/wan-benchmark/README.md).

- LightWan: revisión `f4b91eba1fd7dc2eb038191e2bca7ed33423032d`.
- GGUF QuantStack: `6c6717459277b9cd1f72579d78a0fd62a79e57dc`.
- Componentes Comfy-Org/Wan-2.2: `ee6f4a40737a995bf5818954cfce6d59443b0f04`.
- Cargador ComfyUI-GGUF: `6ea2651e7df66d7585f6ffee804b20e92fb38b8a`; paquete gguf 0.17.1 aislado.
- LightX2V consultado: `100ecfec4be2956539f41688cbd7bc96f0528cca`.

Los pesos se verificaron por tamaño y SHA-256. Las herramientas se ejecutan solo de forma manual con autorización GPU explícita y conservan métricas crudas y grafos.
