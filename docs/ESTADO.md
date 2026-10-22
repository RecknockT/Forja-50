# Estado verificable de Forja 50

22 de octubre de 2026 · desarrollo 0.4.6.

## Resolución entrenada con referencia · 0.4.6

- Corrige el ruido introducido en 0.4.5: usar el tamaño exacto de la referencia (una miniatura de Wikipedia de ~0,6 MP) dejaba al modelo fuera de su régimen entrenado de ~4 MP. Ahora el lienzo toma la **proporción** de la referencia ajustada a la resolución entrenada más cercana (2048², 2304×1728, 2496×1664, 2560×1440, 3104×1312 y sus verticales), así que se conserva el encuadre sin perder calidad.
- El límite de resolución admite esas resoluciones (lado hasta 3104) manteniendo el presupuesto de píxeles anterior: el área nunca supera 2048×2048. La animación conserva su tope de 832.
- Pruebas: 90 del conjunto completo y la comprobación de ventana, sin modelos ni GPU. Pendiente la prueba física con «frieren (la elfa del anime)».

## Encuadre de la referencia · 0.4.5

- Con una única imagen de referencia, el resultado ahora usa **su tamaño y su encuadre**, tanto al editar como al crear con referencia, igual que el flujo oficial de HiDream. Antes el lienzo se forzaba a la escalera de calidad y la referencia se rellenaba con bordes replicados: una portada o un paisaje ancho se copiaba como escena y el personaje quedaba chico o genérico.
- El asistente ya no inventa un paisaje cuando la persona solo pidió un sujeto: mantiene al sujeto como protagonista en un plano medio o cercano con fondo simple y neutro, salvo que se pida una escena.
- El selector de referencia aclara que una portada, un logo o un paisaje abierto deja al personaje pequeño y conviene una imagen cercana.
- Pruebas: 90 del conjunto completo y la comprobación de ventana, sin modelos ni GPU. Pendiente la prueba física con «frieren (la elfa del anime)».

## Nombre pedido y edición · 0.4.4

- El detector de nombres descartaba el nombre en frases naturales («la elfa del anime frieren», «una elfa del anime llamada frieren») y la búsqueda no llegaba a ofrecer la referencia; se filtra el relleno en cualquier posición y se conserva el nombre, sin dejar de descartar conceptos generales como «anime de los 90».
- Garantía en código: si la persona nombró un personaje y el asistente no lo escribe, la descripción preparada lo antepone («Featuring \<nombre\>.»), usando el título de la referencia elegida cuando existe. No depende del modelo chico.
- Edición: el resultado usa el tamaño de la imagen elegida, redondeado a múltiplos de 32 y limitado a 2048 de lado, como el flujo oficial de edición de HiDream. Antes se agrandaba al tamaño de la escalera de calidad y se rellenaba con bordes replicados, lo que podía introducir artefactos.
- Pruebas: 90 del conjunto completo y la comprobación de ventana, sin modelos ni GPU. Pendiente la prueba física de edición; el autor recomienda el modelo Full para editar y sigue sin descargarse por defecto.

## Compatibilidad del dibujo de la interfaz · 0.4.2

- Ante una captura con grandes bloques blancos y controles parcialmente visibles, la interfaz WPF usa renderizado por software desde el inicio. Es una mitigación del camino gráfico acelerado; la captura por sí sola no identifica el desencadenante ni el controlador responsable.
- La preferencia se aplica solo al proceso de interfaz: no modifica el registro, los controladores, las descargas ni la ejecución CUDA de los motores. WebView2 conserva su configuración independiente.
- La prueba de modos comprueba la preferencia en el proceso real. Las capturas internas comprueban el diseño; no sustituyen una comprobación de pantalla del usuario en las condiciones originales del fallo.

## Descripción, referencias y animación · 0.4.3

- El asistente recibía un envoltorio en inglés con el pedido pegado y un prompt de sistema en inglés que prohibía usar nombres propios: con «quiero generar una imagen de frieren de anime» devolvía una escena genérica. Se reescribieron las instrucciones en español (el idioma de la persona, como hace el autor de HiDream) exigiendo la salida en inglés, con reglas explícitas: descartar fórmulas de encargo, conservar textualmente cada nombre propio, título y texto entre comillas, conservar los rasgos del personaje nombrado, no agregar nombres no pedidos, mantener los estilos generales generales y escribir las exclusiones como ausencias. Hay cuatro prompts según la tarea: crear sin referencia, escena nueva con referencia, edición acotada y animación.
- El título de la referencia elegida (por ejemplo «Frieren (character)») ahora entra al asistente y no solo la imagen. Muestreo más fiel: temperatura 0,5, `top_p` 0,9 y `min_p` 0,02 (antes 0,7/0,95/0,05); se conservan 512 tokens y la semilla. La ventana de revisión permite corregir el texto antes de la difusión.
- Referencias en línea: se descartan las páginas de desambiguación de Wikipedia (`pageprops`). Evita que una palabra común («casa», «sol») se convierta en una referencia ajena y que aparezca el diálogo con una foto que no corresponde.
- La referencia usada se muestra como información y deja de ser el metadato de la siguiente: antes, tras generar, el título de Wikipedia quedaba asociado a tu archivo local y el asistente recibía un nombre que no correspondía.
- La animación también usa al asistente: prepara la instrucción de movimiento en inglés (Wan está entrenado en inglés) con revisión editable, o sin ventana si la revisión está desactivada. Antes el texto en español iba crudo al codificador.
- Calidad: la escalera pasa a «Prueba rápida 1024 (menos fiel)», «Equilibrada 1536» y «Recomendada 2048». HiDream está entrenado a ~4 MP y su propio nodo advierte que bajar resolución degrada la fidelidad. Al desactivar el asistente se avisa que HiDream recibe el texto tal cual y conviene escribir en inglés.
- Pruebas: 89 del conjunto completo y la comprobación de ventana, sin modelos ni GPU. Pendiente la prueba física con pedidos reales en las tres placas.
- Límite conocido: el asistente es Gemma 4 E4B INT8; el agente oficial del autor usa Gemma 4 31B. El texto preparado sigue siendo editable antes de generar.

## Edición de HiDream con el programador del autor · 0.4.3

- Al editar (o al crear una escena nueva desde una imagen) HiDream recomienda su programador de edición para Dev, que corresponde a Euler de flujo sin la reinyección de ruido por paso del camino destilado. Forja lo aplica cuando hay una única imagen de referencia (`KSamplerSelect` con `euler`); crear sin referencia conserva `SamplerLCM`. Se mantienen 28 pasos, CFG 1, la escala de ruido inicial y la referencia en `HiDreamO1ReferenceImages`. Motivo: el muestreador destilado aplicado a una edición producía ruido.
- El autor además recomienda el modelo Full para editar; no se descarga por defecto ni se anuncia como probado. La escalera exacta `DEFAULT_TIMESTEPS` del autor no existe como nodo en ComfyUI: se conserva `BasicScheduler normal` con el shift del modelo, que la reproduce de forma aproximada.
- Pruebas de grafo y ventana sin modelos. Pendiente la prueba física de la edición en las tres placas; las cifras históricas de 0.4.1 corresponden al camino anterior.

## Correcciones de HiDream y limpieza · 0.4.1

- Cargador propio del asistente: selecciona explícitamente la GPU del motor y permite descargar pesos a RAM entre etapas. Conserva LOW_VRAM para Wan/difusión; evita la ruta genérica que colocaba Gemma en CPU. Prueba ejecutando el cargador real con adaptadores: selección GPU 2, offload CPU, liberación previa y rechazo de fallback silencioso a CPU.
- Preparación acotada a cinco minutos; cancelación voluntaria diferenciada de timeout. Detección de cierre del proceso y espera de conexión de progreso acotada. Texto limitado a 512 tokens; descripción revisada vacía o excesiva rechazada antes de enviar la tarea.
- El tiempo del motor incluye la preparación de la descripción usada, sin contar el tiempo de revisión humana. Cancelar la revisión se describe como cancelación de imagen, no como ausencia de uso de GPU.
- Cálculo de almacenamiento cancelable al cerrar: permite continuar la desinstalación. Se reutilizan comprobaciones de ancestros dentro del cálculo, pero cada borrado conserva validación fresca de enlaces, repositorios e importaciones.
- Pruebas locales sin modelos: núcleo, puente y ventanas. Los paquetes se generan mediante CI. No se reinstala la app ni se descargan modelos en la PC del usuario.
- Pruebas físicas históricas de HiDream Dev FP8 sin asistente en RTX 5080/32 GB: 1024 en 15,8 s (primera carga), 2048 en 19,8 s y edición a 2048 en 48,9 s; pico total muestreado 11918 MiB incluyendo otras aplicaciones. La edición cambió también iluminación y otras partes de la ropa. No son cifras del asistente corregido ni garantías de calidad/identidad. Pendiente medir el nuevo cargador y validación física 5070 Ti/3090.

## HiDream-O1 Dev · 0.4.0 en desarrollo

- Sustituye la preparación no publicada de FLUX 3: descarga pública de HiDream sin token/cuenta, checkpoint nativo FP8 escalado para 5080/5070 Ti y BF16 para 3090. Licencia MIT, revisión y hash fijados.
- Flujo nativo de 28 pasos y CFG 1: muestreador destilado (LCM) al crear sin referencia y Euler de flujo al editar con una referencia. Referencia real a HiDream, descripción original/preparada y tiempos conservados en el historial. Asistente Gemma 4 E4B INT8 opcional; liberación entre etapas. Calidad 1024/1536/2048, con formatos independientes de Wan.
- Revisión previa de la descripción: con el asistente activo y la revisión habilitada (valor inicial), Forja ejecuta solo la etapa de texto y muestra el resultado editable antes de cargar la difusión. Se puede regenerar, volver al pedido original o cancelar antes de iniciar la difusión; el texto final es el que recibe HiDream y queda en el historial, sin repetir el asistente. Con la revisión desactivada se conserva el flujo de una sola pasada. Pruebas de grafo sin asistente duplicado, contrato HTTP de preparación y revisión real de la ventana; la captura de pantalla y la validación física siguen pendientes.
- Modelos anteriores conservados y registrados para limpieza explícita. Instalador sin pesos; importación verificada, pausa y reanudación existentes. Licencia HiDream separada de aceptaciones anteriores.
- Compilación y núcleo aprobados; cinco contratos de grafos aprobados contra ComfyUI real en CPU, sin pesos. Pendientes pruebas físicas, interfaz y paquete de esta versión. No equivale a validación en las tres placas.
- [Investigación y plan](HIDREAM-MIGRACION.md). Dev-2604 sigue como candidato de comparación; no es el checkpoint Dev original de Comfy ni se anuncia como probado.

## Interfaz compartida y VRAM 0.3.6

- Programación e imágenes comparten Iniciar IA y Detener arriba a la derecha. Pausar/Cancelar también queda en el encabezado, accesible durante cualquier operación. No hay un segundo botón de inicio en la pantalla visual.
- Modelos y descargas plegados cuando están preparados; descripción, acción principal y resultado visibles en la ventana habitual. Ayuda según Crear/Editar/Animar; selección de imagen cerca de la descripción y opciones de referencia solo donde se usan.
- VRAM usada/libre y barra de ocupación en ambos modos, consulta cada cuatro segundos también mientras se genera. Valores de NVIDIA para la placa completa, incluyendo otras aplicaciones; no se atribuye ese total solo a la IA. Memoria reservada no se cuenta como usada; mediciones inválidas se muestran como no disponibles, sin inventar ceros. Consulta cancelable y sin solapamientos.
- Pruebas de regresión del encabezado, modos, cierre, cancelación y mediciones; revisión de interfaz real y cambio de modo. La prueba física de esta PC corresponde a RTX 5080; no valida 5070 Ti ni 3090.
- Prueba desde la interfaz: inicio, imagen de ejemplo completada y animación de esa imagen completada. GIF y MP4 decodificados con 73 fotogramas a 576×576 y MP4 a 24 FPS. Durante el trabajo se observaron lecturas de 9,4 GiB usados/6,2 libres (imagen) y 14,2/1,4 (video); son muestras del total de la placa, no picos certificados ni consumo exclusivo de Forja. Se revisaron también el diálogo Guardar y el rechazo del cierre.

## Nombre y descripción del personaje 0.3.5

- El pedido «frieren la elfa del anime, volando con su baston magico» se generó sin referencia en 0.3.4: la consulta incluía la descripción y no coincidía con la ficha. Corregida la separación del nombre y las aposiciones («la elfa», «del anime», «the elf»), conservando nombres compuestos y el pedido original.
- El flujo de una escena nueva con referencia indica conservar identidad, cabello, vestimenta y accesorios característicos; editar conserva su pedido independiente. Las pruebas verifican nombre, referencia real y grafo.
- La referencia utilizada o la continuación sin referencia quedan visibles después de generar, también en el aviso principal. La detección sigue siendo conservadora; no es un LLM interpretando cualquier descripción.
- Prueba física en esta RTX 5080 con el texto y semilla del fallo: generación completada con referencia registrada «Frieren (character)». La inspección mostró cabello plateado, orejas élficas y vestimenta/báculo cercanos a la referencia; no garantiza identidad perfecta ni valida otros personajes o placas. Se conservó el resultado en el historial del usuario. La prueba explícita `--reference-probe --generate-reference --data-root` requiere datos existentes, licencias aceptadas y GPU disponible; nunca se ejecuta en la batería normal o en CI.

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
