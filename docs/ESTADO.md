# Estado verificable de desarrollo

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
