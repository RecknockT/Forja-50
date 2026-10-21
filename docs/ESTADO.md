# Estado verificable de Forja 50

21 de octubre de 2026 · desarrollo 0.2.0.

## Integración nativa y descargas

- Aplicación .NET 10/WPF compilada sin advertencias ni errores.
- 44 pruebas de núcleo y 6 comprobaciones de ventana/configuración aprobadas. Cubren perfil nativo, selección de GPU/dispositivo, preservación de ajustes manuales, argumentos, artefacto fijado, caché, integridad, pausa y reanudación de fragmentos fuera de orden, espacio libre al reanudar, conexiones estancadas, cierre y migración con copia de los ajustes anteriores.
- Configuración nativa inicial: Qwen3.8-27B oficial de Todd, contexto/KV 64K, Q4, MTP-3, embeddings en RAM y visión desactivada. Prefill 896 en 5080 y 512 en 5070 Ti. Son puntos de partida, no perfiles anunciados como óptimos o físicamente validados en Forja.
- Descargador de 16 conexiones sin límite artificial de MB/s, fragmentos persistentes de 64 MiB y velocidad reciente. Solicitudes reales al servidor del modelo respetaron Range.
- Medición acotada de los mismos 64 MiB en esta conexión: una conexión 18,79 MB/s (3,58 s); dieciséis 24,18 MB/s (2,78 s). No es un benchmark del archivo completo ni una velocidad garantizada.
- Vista previa nativa y configuración inicial revisadas con datos aislados, sin cargar el modelo ni iniciar inferencia.
- Motor Windows compilado y publicado desde la revisión aee9106. Paquete de 243.616.338 bytes verificado por tamaño, SHA-256, procedencia y licencias; `ninfer-serve.exe --help` aprobado en esta PC sin cargar pesos. Preparación automática integrada, con importación opcional y restauración al motor administrado.
- Prueba física de inferencia del nuevo backend: pendiente por pedido del usuario en ambas placas. Los resultados históricos de abajo corresponden a KVMem/Swift.

## Pendientes de producto

Pruebas de inferencia y uso prolongado del backend nativo; instalación en Windows limpio; preparación automática de WebView2; firma de editor y elección de licencia del código propio de Forja.

## Alcance de la investigación

Ver [NInfer en Windows](NINFER-WINDOWS.md) para distinguir el port Windows/MSVC de los resultados de WSL2 y del runtime Linux v1.5. Las cifras de terceros conservan sus condiciones de prueba y no se anuncian como mediciones de Forja.

---

# Historial de validación · backend KVMem

20 de octubre de 2026.

- Solución .NET 10/WPF compilada sin advertencias ni errores.
- Veintiuna pruebas de núcleo aprobadas: hardware y perfiles por placa, ZIP, descargas e integridad con recuperación de 416, rutas Unicode y portable movida, recuperación de ajustes dañados, almacenamiento en unidades separadas, proveedor local, contexto 64K, alternativa 32K, ajustes avanzados, decimales independientes de la región y procesos Windows.
- Componentes de desarrollo descargados y verificados. El modelo de referencia se importa, sin duplicar sus 12,8 GB.
- Interfaz de preparación renderizada para revisión visual.
- Scripts reproducibles para compilar, probar y publicar paquete win-x64 autocontenido. Definición de instalador Inno Setup por usuario con un acceso directo.
- Paquete portable autocontenido generado (aproximadamente 66 MB, sin modelo). El ejecutable publicado abrió con datos nuevos y generó su captura de interfaz; salida 0. Esto verifica el arranque de la ventana, no una sesión de inferencia.
- Prueba de integración aprobada en RTX 5080: agente con lectura, edición, PowerShell, rama y commit en repositorio aislado; árbol limpio; detención y reinicio de motor y agente. Resultado privado en `.dev-data/logs/integration-result.json`.
- Perfil 64K probado en RTX 5080 con entradas de 43.345 y 52.949 tokens, recuperación 3/3 en ambas y cierre del proceso al terminar. Ver `CONTEXTO.md` para consumo, tiempos y límites de la prueba.
- Chat integrado aprobado con autenticación local y modelo Qwen visible, usando 64K. Captura revisada. La prueba inicial usaba un mínimo arbitrario de caracteres y se corrigió para verificar el contenido real de la interfaz.
- Tema oscuro, panel de parámetros y configuración avanzada con restauración. Portable con datos junto al ejecutable; Setup con datos por usuario. Predeterminado de 64K solicitado por el usuario.
- ZIP extraído y probado: arranque, creación automática de datos junto al ejecutable y 64K. Setup compilado y probado en esta PC: instalación por usuario en carpeta aislada, arranque de la ventana y desinstalación, todo aprobado. No sustituye una prueba en Windows limpio.
- Acceso anterior del usuario comprobado después del cambio: motor y agente configurados en 64K, consulta breve correcta y detención al terminar.
- Corregido el error de la primera X con IA detenida: se reprodujo la excepción de WPF antes del arreglo. El cierre ahora se completa desde el Dispatcher después de cancelar el primer evento, espera la operación activa y bloquea solicitudes simultáneas. Cuatro regresiones reales de ventana aprobadas: primera X, cierre repetido, cancelación de operación al cerrar y rechazo de contexto incompatible. Se ejecutan también antes de empaquetar en GitHub.
- Revisión posterior: corregidos parciales completos de tamaño desconocido, bloqueo por HTTP 416, rutas predeterminadas de portable movida, validación de ambas unidades de almacenamiento, configuración dañada al arrancar, cambio de contexto incompatible y legibilidad de la licencia en tema oscuro. El modelo y su dispositivo forman parte de la identidad del motor cargado.
- Una versión del programa selecciona perfiles independientes para RTX 5080 y RTX 5070 Ti. Ambos parten actualmente de 64K y los mismos presupuestos de memoria. Solo 5080 tiene prueba física básica; la detección no cambia una configuración manual previa.

Pendientes antes de distribuir:

1. Completar sesiones prolongadas con 64K y pruebas cerca del límite.
2. Probar el instalador en Windows limpio, preparar WebView2 automáticamente, acordar licencia del proyecto y revisar condiciones de redistribución de componentes; firma del instalador.
3. Probar en una RTX 5070 Ti de escritorio física.

El usuario pidió retirar la instalación inicial y dejar solo el repositorio local. Hará manualmente la instalación y descarga del modelo desde cero. Los resultados anteriores de GPU quedan como registro histórico. No se considera una versión final de producción.
