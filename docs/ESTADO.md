# Estado verificable de Forja 50

21 de octubre de 2026 · desarrollo 0.3.4.

## Conceptos generales y animación 0.3.4

- «Anime de los 90» y otros conceptos visuales generales se pasan al modelo sin imponer una ficha de Wikipedia. La consulta automática es conservadora y usa solo coincidencias claras; sin coincidencia o red disponible continúa con el texto. El selector de fichas queda reservado a una búsqueda escrita expresamente por el usuario. No hay un LLM conversacional clasificando la intención.
- Wan usa su propio texto negativo codificado para defectos visuales, en lugar del ConditioningZeroOut que corresponde al flujo FLUX. Se conservan shift 8, CFG 5, uni_pc/simple, 20 pasos y 24 FPS de la plantilla oficial; la descripción original se conserva y, con imagen inicial, se añaden indicaciones para mantener identidad, geometría y estilo.
- Animación cuadrada 576×576, horizontal 768×448 y vertical 448×768, sin aumentar el presupuesto de píxeles anterior. El marco inicial se ajusta conservando proporciones con bordes extendidos; no se recorta ni se fuerza a horizontal. Nuevas pruebas de grafo, interfaz y ajuste real en CPU.
- Revisión del MP4 aportado: 73 fotogramas a 24 FPS; deforma y cambia los colores del auto en lugar de hacerlo avanzar. Guardar GIF/MP4 funciona en la prueba del usuario, pero eso no valida calidad de movimiento. La mejora de calidad de este flujo necesita repetir una generación real; no se garantiza una trayectoria ni un bucle perfecto.
- Plantilla contrastada: https://github.com/Comfy-Org/workflow_templates/blob/main/templates/video_wan2_2_5B_ti2v.json

## Referencias en línea y exportación 0.3.3

- Crear imagen puede buscar una referencia en Wikipedia (opción visible y desactivable). Detección sencilla del nombre, término editable, preferencia por fichas de personajes y selección cuando no hay coincidencia clara. No es un buscador de toda la web ni otro LLM.
- Imagen real enviada a FLUX sin sustituir el pedido original. Fuente y término persistidos en el trabajo; editar/animar conservan la referencia elegida, sin búsquedas nuevas. Sin claves, sin cuenta y sin acceso a credenciales GitHub.
- Consulta y descarga acotadas, HTTPS, dominios permitidos, sin redirecciones, PNG/JPG comprobados, cancelación y tiempos de espera. Si falla la búsqueda, se informa antes de generar; se puede desactivar para trabajar sin conexión.
- Corregido el formato DynamicCombo de SaveVideo: la API requiere entradas planas `format` y `format.codec`. El fallo observado ocurrió después de los 20 pasos de Wan, al exportar; no justificaba el aviso genérico de memoria.
- Verificada exportación MP4/H264 con SaveVideo real y ocho fotogramas sintéticos, sin inicializar CUDA. Los errores ahora muestran nodo y detalle; solo los de memoria recomiendan reducir consumo.
- Las generaciones realizadas por el usuario en la 5080 no validan la 5070 Ti ni la 3090 ni establecen un pico medido de VRAM.

## Interfaz visual 0.3.2

- Preparación, creación y resultado separados; diseño adaptable y contenido centrado en pantallas anchas.
- Acciones Crear imagen, Editar imagen y Animar visibles; ejemplo de descripción y vista previa de referencia.
- Detalles técnicos plegados, historial oscuro y estado vacío explicativo.
- Pausa de descarga y cancelación visibles dentro del modo visual; pruebas de regresión sin iniciar modelos.
- La generación y los picos de VRAM siguen pendientes de medición física.

## Corrección de empaquetado 0.3.1

- Corregida la ruta de licencias desde el proyecto WPF. La publicación comprueba que los avisos completos estén presentes antes de crear Setup/portable. La revisión del ZIP 0.3.0 detectó su ausencia; 0.3.1 reemplaza esa entrega.

## Dos modos independientes (implementación inicial)

- Selector Programación / Imágenes y animación. Programación conserva sus modelos, Git y OpenCode; el modo visual no los requiere. Ajustes separados y un único modo activo, con confirmación antes de detener y cambiar.
- ComfyUI portable NVIDIA 0.42.1 fijado con SHA-256/tamaño. Descarga de FLUX.2 Klein 4B BF16, codificador Qwen3 4B y VAE; Wan 2.2 TI2V-5B y sus componentes son opcionales. Revisiones inmutables, tamaños y hashes de cada peso; preparación con descarga reanudable, comprobación de espacio y extracción 7z validada.
- Pantalla visual propia: descripción, referencia, imagen nueva, resolución, duración, semilla y pasos, galería y exportación PNG/MP4/GIF. La última imagen se ofrece como referencia de edición; no se usa un LLM conversacional en este modo.
- Motor solo en loopback con credencial efímera, comprobación del puente propio, rechazo de Origin y nodos externos deshabilitados. GIF con Pillow y MP4 con PyAV del motor; no se instala un FFmpeg separado.
- Trabajos persistentes; cancelación detiene procesos propios y registra el estado. Recuperación sin relanzar trabajos y rutas de salida relativas para la portable. Limpieza protege creaciones y referencias.
- Comprobaciones locales aprobadas: 74 del núcleo, 14 de ventanas/configuración, 2 del puente Python y una exportación GIF real de ocho fotogramas sintéticos. Compilación sin advertencias ni errores. Se contrastaron los argumentos de arranque con el código de ComfyUI fijado y se revisó la vista renderizada.
- Sin inferencia física ni descarga de pesos durante el desarrollo. Consumo, rendimiento, descarga/extracción del portable completo y flujos reales del motor con las tres placas siguen pendientes. Las comprobaciones automáticas no certifican esas pruebas.

## Carpetas con el nombre público

- Nuevas instalaciones: programa en `%LOCALAPPDATA%\Programs\Forja 50`; datos y modelos en `%LOCALAPPDATA%\Forja 50` y su subcarpeta `models`.
- Actualizaciones: mismo AppId y ejecutable; Inno reutiliza la carpeta anterior. Si existe `%LOCALAPPDATA%\MiIALocal`, la aplicación sigue usando esos datos sin moverlos, duplicarlos ni invalidar rutas guardadas. Portable y `--data-root` conservan su ubicación explícita.
- El desinstalador detecta ambas carpetas de datos antes de ofrecer la limpieza. No se renombran carpetas de la instalación del usuario desde herramientas de desarrollo.

## Cuenta GitHub, paquetes y cierre

- Apartado Cuenta de GitHub: prepara Git/GCM sin modelo ni inferencia, OAuth mediante navegador, estado de cuenta local y cierre de sesión propio. GCM 3.0.1 fijado por SHA-256 y tamaño. Almacén Windows DPAPI separado, sin modificar Git global ni registrar salida privada de autenticación. El agente usa el helper únicamente para GitHub HTTPS; SSH y autenticación externa sin sesión de Forja conservan su flujo.
- Sesión de GitHub como sexta categoría de almacenamiento. Cerrar sesión borra solo su almacén/caché de cuenta y no revoca permisos en GitHub. Se conservan proyectos y credenciales de otros programas. Preparar la IA restaura el gestor si una sesión guardada lo requiere.
- Repositorio canónico `RecknockT/Forja-50`; Setup y portable pasan a llamarse `Forja-50-VERSION-Setup.exe` y `Forja-50-VERSION-Portable-win-x64.zip`. El nuevo actualizador acepta nombres y URLs anteriores conocidos, manteniendo validación de origen/tamaño/hash. Las versiones hasta 0.2.2 pueden necesitar una actualización manual por su validación estricta del nombre anterior del repositorio.
- Corregido el cierre al limpiar conversaciones/ajustes: libera el navegador, espera su salida, reemplaza el control descartado y reinicia la vista sin cerrar Forja. Al eliminar ajustes restaura valores iniciales; los proyectos permanecen en su carpeta.
- Cerrar Forja siempre pide confirmación con No inicial. Rechazar conserva operaciones; confirmar pausa/cancela preparación y detiene procesos propios antes de salir. Un rechazo también descarta una actualización pendiente, evitando abrir su instalador en un cierre posterior.
- 60 comprobaciones del núcleo y 13 de ventanas/configuración aprobadas, sin advertencias ni errores de compilación: sesión, migración de paquetes, limpieza completa, reinicio de vista y cierre aceptado/rechazado. Prueba adicional local con GCM y Git reales: credencial ficticia cifrada con DPAPI en una ruta con espacios, Unicode y caracteres especiales; helper HTTPS y cierre de sesión aprobados sin OAuth ni acceso a repositorios. Prueba con WebView2 vacío: borrar todo y volver a crear el navegador sin cerrar la aplicación aprobada. No se descargaron pesos ni se inició inferencia.
- Autorización OAuth real: pendiente de que el usuario complete el navegador. No se tomó control de su sesión de GitHub ni se instaló Forja durante estas pruebas.

## RTX 3090 y Setup unificado

- Tercer perfil de escritorio: RTX 3090 de 24 GB, KVMem sm86 y Swift. Contexto inicial 128K, historial GPU seleccionado 96K, reserva 16K, caché RAM 8 GiB y prefill 512. Alternativas 64K/32K con presupuestos adecuados al contexto. Los perfiles RTX 50 conservan sus valores nativos iniciales.
- Descarga del motor RTX 30 fijada por tamaño y SHA-256, con ruta separada del paquete RTX 50. Detección, selección del paquete, preparación y arranque protegen la compatibilidad. El selector nativo sm120a queda deshabilitado para la 3090.
- 55 comprobaciones del núcleo y 9 de ventanas/configuración aprobadas: incluyen los tres perfiles, conservación de ajustes, cambio de contexto y rechazo del motor incompatible. Compilación sin advertencias ni errores. No se cargaron pesos ni se inició inferencia.
- ZIP RTX 30 descargado y verificado contra su SHA-256/tamaño publicados. BUILD-INFO indica arquitectura 86, dependencias y validación física pendiente. Ruta interna verificada; `ninfer-kvmem-server.exe --help` aprobado localmente sin modelo.
- Setup y portable 0.2.2 generados localmente. Setup agrupa carpeta, acceso al escritorio (marcado de inicio) y abrir al terminar. Se omiten las páginas separadas de tareas, carpeta del menú Inicio y resumen; el acceso al menú Inicio permanece automático. El instalador no descarga pesos ni inicia IA por sí mismo.
- Pantalla del Setup comprobada visualmente: carpeta, ambas casillas marcadas y botón Instalar en una sola vista. El Setup se canceló antes de instalar.
- Perfil 3090 pendiente de validación física y de uso prolongado. Los presupuestos son valores iniciales del producto, no mediciones de consumo ni garantía de rendimiento. Ver [perfil 3090](RTX3090.md).

## Estado posterior de la PC y revisión técnica

Por nuevo pedido del usuario se retiraron la instalación manual, sus datos y el instalador antiguo descargado. La comprobación final confirmó que no quedaban las carpetas de aplicación/datos ni procesos propios, y que el checkout seguía presente. No reinstalar por ahora. Las referencias históricas a conservar la instalación manual describen pruebas anteriores.

La [revisión del 21 de octubre](REVISION-2026-10-21.md) compara la base actual con otros motores Windows, modelos de menor tamaño y versiones posteriores de dependencias. Es investigación documental; no se cambiaron perfiles ni se probaron nuevos motores en la GPU.

## Limpieza y desinstalación

- Administrar almacenamiento: cinco categorías con espacio y rutas, selección inicial vacía, selección de todo lo permitido y confirmación con No como respuesta inicial. La IA debe estar detenida.
- El desinstalador interactivo ofrece abrir esa misma selección antes de quitar el programa. Conservar es la opción inicial; la desinstalación silenciosa conserva los datos.
- El borrado se limita a modelos conocidos, componentes, descargas/parciales, actualizaciones, datos del agente/navegador, ajustes y registros. Se protegen importaciones, proyectos, repositorios Git y enlaces. Se vuelve a validar el plan antes de borrar, sin seguir enlaces durante el recorrido.
- 53 pruebas de núcleo y 8 comprobaciones de ventana/configuración aprobadas. Las nuevas cubren selección por categoría, cancelación, instancia ocupada, repositorios anidados, worktrees, proyecto sin Git, importaciones, junction agregada tras inspección y preservación de archivos ajenos.
- Prueba local adicional aprobada: iniciar un navegador integrado vacío, cerrarlo, esperar la salida de sus procesos y borrar su caché. Sin carga de modelo ni inferencia. Setup 0.2.1 compilado; no se instaló sobre la instalación manual del usuario.
- [v0.2.1-dev publicada](https://github.com/RecknockT/Forja-50/releases/tag/v0.2.1-dev) desde `2508014c8fc5c42a770d86ee2b1474f66b3a0ac5`, con CI de main y etiqueta aprobados. Setup, portable y manifiesto descargados y verificados por tamaño/SHA-256. La ventana de limpieza del portable publicado conservó los datos al cancelar y rechazó una instancia ocupada; el actualizador público detectó ambos paquetes.

## Integración nativa y descargas

- Aplicación .NET 10/WPF compilada sin advertencias ni errores.
- 45 pruebas de núcleo y 6 comprobaciones de ventana/configuración aprobadas. Cubren perfil nativo, selección de GPU/dispositivo, preservación de ajustes manuales, argumentos, artefacto fijado, caché, integridad, pausa y reanudación de fragmentos fuera de orden, espacio libre al reanudar, conexiones estancadas, cierre y migración con copia de los ajustes anteriores.
- Configuración nativa inicial: Qwen3.8-27B oficial de Todd, contexto/KV 64K, Q4, MTP-3, embeddings en RAM y visión desactivada. Prefill 896 en 5080 y 512 en 5070 Ti. Son puntos de partida, no perfiles anunciados como óptimos o físicamente validados en Forja.
- Descargador de 16 conexiones sin límite artificial de MB/s, fragmentos persistentes de 64 MiB y velocidad reciente. Solicitudes reales al servidor del modelo respetaron Range.
- Publicación y lectura de fragmentos coordinadas para evitar aperturas durante el renombrado en Windows. La regresión del prefijo anterior ejecuta cien reanudaciones concurrentes con verificación de contenido.
- Medición acotada de los mismos 64 MiB en esta conexión: una conexión 18,79 MB/s (3,58 s); dieciséis 24,18 MB/s (2,78 s). No es un benchmark del archivo completo ni una velocidad garantizada.
- Vista previa nativa y configuración inicial revisadas con datos aislados, sin cargar el modelo ni iniciar inferencia.
- Motor Windows compilado y publicado desde la revisión aee9106. Paquete de 243.616.338 bytes verificado por tamaño, SHA-256, procedencia y licencias; `ninfer-serve.exe --help` aprobado en esta PC sin cargar pesos. Preparación automática integrada, con importación opcional y restauración al motor administrado.
- [v0.2.0-dev publicada](https://github.com/RecknockT/Forja-50/releases/tag/v0.2.0-dev): Setup, portable y manifiesto generados desde `cd74f748f1cdf28bfad3ebd4e86f054d342b8ad1`, con los controles de main y etiqueta aprobados. Se descargaron los tres assets y se verificaron tamaño, SHA-256 y coincidencia con el manifiesto.
- Portable publicado abierto con datos nuevos: motor nativo seleccionado, 64K y visión apagada; sin pesos incluidos ni inferencia. El actualizador público detectó tanto Setup como portable de 0.2.0-dev con sus hashes. El Setup nuevo fue compilado y verificado, pero no se instaló sobre la instalación manual del usuario.
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
