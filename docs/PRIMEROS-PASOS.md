# Primeros pasos con Forja 50

Versión de desarrollo para Windows 11 x64, RTX 5070 Ti / RTX 5080 de escritorio (16 GB) o RTX 3090 (24 GB), con 32 GB de RAM. La prueba de inferencia de los nuevos perfiles en Forja está pendiente.

1. Instalá el Setup, o extraé el ZIP portable y ejecutá `app\Mi IA Local.exe`. No hace falta instalar CUDA ni compiladores.
2. Revisá la placa detectada. El modo nativo selecciona 64K y prefill 896 en la 5080, o 512 en la 5070 Ti. La 3090 selecciona KVMem para RTX 30, contexto 128K y prefill 512; su prueba física sigue pendiente. La configuración activa aparece en el panel izquierdo.
3. Elegí dónde guardar el modelo: Qwen de Todd (16,5 GB) en el modo nativo, o Swift (12,8 GB) en KVMem/3090. Reservá el espacio que indique el programa para preparar modelo, motor y herramientas.
4. Revisá las licencias y pulsá **Preparar IA**. Se descargan motor y modelo automáticamente. El descargador usa 16 conexiones y muestra GB, MB/s recientes y tiempo restante aproximado.
5. **Pausar** conserva los bytes y fragmentos guardados. **Reanudar preparación** continúa incluso después de cerrar. No borres el `.part` ni la carpeta `.chunks-*` mientras la descarga esté incompleta.
6. Elegí la carpeta de tu repositorio local con **Elegir proyecto**. No se copia tu repositorio.
7. Cuando quieras hacer la prueba, cerrá otras aplicaciones que usen mucha VRAM y pulsá **Iniciar IA**. La IA no arranca automáticamente al instalar, descargar o configurar.
8. **Detener** libera la memoria usada por la IA. Al cerrar Forja aparece una confirmación con **No** como opción inicial. **Sí** pausa descargas, detiene sus procesos propios y cierra la aplicación. **No** mantiene la aplicación y sus operaciones activas. Los archivos y chats se conservan.

El Setup reúne carpeta de instalación, **Crear acceso directo en el escritorio** (marcado por defecto) y **Abrir Forja al terminar** en una sola pantalla. El acceso al menú Inicio se crea automáticamente. La carpeta del modelo se elige luego en Forja y no inicia ninguna descarga durante la instalación. El modo silencioso no abre la aplicación y respeta la selección de tareas indicada en sus argumentos.

## Configuración avanzada

Con la IA detenida, podés cambiar contexto, prefill, razonamiento, salida máxima, MTP, temperatura, CUDA Graph y presupuesto de visión. Prefill 0 recupera el valor automático por placa. La visión viene desactivada para dar prioridad al código. 128K es experimental en Windows nativo y puede no entrar en la memoria libre. Un valor numéricamente válido no garantiza que entre en VRAM.

La 3090 tiene opciones KVMem de 128K, 64K y 32K. Al cambiar desde los valores del perfil, se ajustan también historial y reserva para que quepan en el contexto elegido. Las personalizaciones incompatibles requieren corregirse en Configuración avanzada. **Restaurar recomendada** recupera los valores específicos de la placa. El motor sm120a de las RTX 50 permanece deshabilitado en la 3090.

## Cuenta de GitHub

Con la IA detenida, abrí **Cuenta de GitHub**. **Conectar con GitHub** prepara Git portable y Git Credential Manager oficiales (sin modelos de IA) y abre la autorización en tu navegador. Completá vos el inicio de sesión y los permisos en GitHub. Forja no pide tu contraseña. El apartado muestra la cuenta guardada localmente; GitHub verifica permisos y vigencia al operar sobre cada repositorio.

La sesión se guarda cifrada mediante Windows DPAPI en `github-auth`; `github-account.json` contiene únicamente el nombre público de cuenta. Se usa un almacén separado de otros programas. El agente recibe la configuración del gestor para GitHub por HTTPS, sin inyectar un token en su configuración ni guardarlo en registros. No se modifica tu configuración global de Git. Si no conectás una cuenta de Forja, conserva la autenticación externa que ya uses.

**Cerrar sesión** elimina la sesión local de Forja; no revoca la autorización de la aplicación en GitHub ni cierra otras herramientas. También aparece como categoría independiente en Administrar almacenamiento y en la limpieza antes de desinstalar. Para cambiar de cuenta, cerrá la sesión y conectá de nuevo. La sesión portable está vinculada al usuario de Windows que la creó: en otro usuario/PC hay que autorizar de nuevo.

Esto no cambia el autor/correo de los commits ni las claves SSH. Los repositorios SSH mantienen su método de autenticación. Conectar no crea repositorios ni sube archivos; el agente conserva su solicitud de permiso para `git push`.

## Si venís de una versión anterior

Las instalaciones anteriores conservan su selección KVMem/Swift. Elegí **NInfer nativo Windows · modelo oficial de Todd** para pasar al nuevo backend, revisá sus licencias y prepará la IA. Se preservan los archivos y la configuración anterior.

El modelo de Todd y Swift son archivos diferentes: un parcial de Swift no puede reutilizarse como parte del modelo de Todd. Forja conserva cada uno por separado. Los parciales del mismo modelo sí se pueden retomar desde versiones anteriores del descargador.

Si la descarga actual está trabada, pulsá Cancelar o Pausar y esperá a que se detenga antes de actualizar. Después usá **Buscar actualizaciones**. El actualizador verifica el paquete antes de abrir el instalador; en portable descarga el ZIP y explica cómo conservar `datos`.

## Datos y diagnóstico

## Imágenes y animación (0.3.0 experimental)

Elegí **Imágenes y animación** en Modo de uso. No hace falta elegir un proyecto ni instalar Qwen de programación, OpenCode o Git. Abrí Preparación y descargas, revisá las licencias y pulsá Descargar y preparar. La descarga inicial completa ronda 18,2 GB incluyendo ComfyUI, FLUX.2 Klein 4B, codificador y VAE; lo descargado se reutiliza. Marcar animaciones agrega unos 18,1 GB de pesos de Wan y sus componentes. El programa comprueba además espacio de extracción. Las descargas pueden pausarse y reanudarse.

Pulsá **Activar modo imagen**. Elegí **Crear imagen**, **Editar imagen** o **Animar**, escribí la descripción y pulsá el botón de esa acción. La primera generación carga los pesos. Una imagen completada queda seleccionada como referencia: el próximo pedido edita esa imagen (por ejemplo, «cambiá solo el brazo izquierdo a rojo»). **Quitar referencia** elimina la referencia sin borrar resultados. Los detalles técnicos y las opciones adicionales se pueden desplegar cuando los necesites. También podés elegir una referencia PNG/JPG externa; Forja copia el archivo y conserva el original. No se promete que cada edición conserve todos los detalles.

Para animar, prepará antes el paquete opcional, elegí Animar, una referencia opcional y la duración. La configuración inicial usa 768 × 448, 24 FPS y 73/121 fotogramas (aproximadamente 3/5 segundos). **Más opciones** permite fijar semilla y pasos de animación. El flujo guarda MP4 y GIF; Abrir usa el visor del sistema y Guardar permite elegir el formato disponible. La vista previa de GIF es estática. No se garantiza continuidad perfecta entre el último y el primer fotograma.

Solo se ejecuta un modo a la vez. Cambiar con una IA activa pide detenerla; rechazar conserva la sesión actual. Cancelar una generación detiene el trabajador y registra el trabajo: se puede repetir, pero no retomar sus pasos intermedios. Los modelos descargados y resultados anteriores permanecen.

Los datos visuales están bajo `visual` dentro de la carpeta de datos: `models`, `inputs`, `jobs` y `creations`. La limpieza de modelos, componentes o historial conserva las creaciones y referencias copiadas. El historial usa rutas relativas para poder mover la portable. La IA visual no tiene acceso a la sesión GitHub de Forja ni herramientas de shell/Git.

**Pendiente:** inferencia real, consumo de VRAM/RAM y rendimiento en las tres placas; no se garantiza que todos los tamaños funcionen con 32 GB de RAM y otras aplicaciones abiertas. Se han comprobado protocolos, flujos, autenticación, GIF sintético, extracción, persistencia, UI y aislamiento sin cargar modelos. Revisá Diagnóstico si la generación informa falta de memoria.

## Ubicación de los datos

Desde 0.2.4 las instalaciones nuevas usan `%LOCALAPPDATA%\Programs\Forja 50` para el programa y `%LOCALAPPDATA%\Forja 50` para los datos (modelos en `models`). Al actualizar se conserva la ubicación del programa y se reutilizan los datos anteriores en `%LOCALAPPDATA%\MiIALocal` si existen; no se mueven ni se vuelven a descargar. La portable guarda `app\datos`. Conservá esa carpeta al mover o actualizar la portable; los modelos importados de una ubicación externa dependen de esa ruta.

## Administrar almacenamiento y desinstalar

Con la IA detenida, abrí **Administrar almacenamiento**. Verás el espacio y las rutas de modelos, componentes, caché/descargas incompletas, conversaciones, ajustes y sesión de GitHub. Podés marcar categorías o usar **Seleccionar todo**; **Eliminar seleccionados** pide una confirmación con No como respuesta inicial. La eliminación es permanente. Borrar componentes permite descargarlos de nuevo; borrar parciales pierde ese progreso. Al borrar conversaciones o ajustes, Forja libera el navegador y reinicia la vista sin cerrar la aplicación. Si borraste ajustes, muestra los valores iniciales para preparar de nuevo la IA.

## Cambio de nombre de los paquetes

Desde 0.2.3 los paquetes se llaman `Forja-50-VERSION-Setup.exe` y `Forja-50-VERSION-Portable-win-x64.zip`, en el repositorio `RecknockT/Forja-50`. La aplicación conserva sus identificadores y AppId, y detecta la carpeta de datos anterior para mantener instalaciones existentes. El nuevo actualizador reconoce ambos nombres de paquetes y los dos nombres conocidos del repositorio.

Las versiones hasta 0.2.2 validaban de forma estricta la URL antigua; tras renombrar el repositorio pueden no detectar esta actualización. En ese caso instalá manualmente 0.2.3 una vez desde la publicación oficial. No hace falta borrar tus datos para actualizar.

Los archivos importados y sus carpetas se conservan. Se bloquea la limpieza de carpetas enlazadas, repositorios Git y la carpeta de proyecto seleccionada; también se conservan archivos desconocidos fuera de las carpetas administradas. Un modelo descargado en una ubicación personalizada aparece por su ruta exacta, sin borrar el resto de esa carpeta.

Al desinstalar con Setup, elegí si querés abrir esta misma limpieza antes de quitar el programa. **No** conserva los datos. **Sí** abre la selección y su confirmación; cerrar esa ventana conserva los datos restantes. La desinstalación silenciosa y las actualizaciones conservan los datos. La limpieza solo se abre si Forja no está ejecutándose.

**Diagnóstico y registros** abre los mensajes de la aplicación. Si falta WebView2 Runtime, instalalo desde la [página oficial de Microsoft](https://developer.microsoft.com/microsoft-edge/webview2/). Su preparación automática sigue pendiente.

El análisis de fuentes y la diferencia entre Windows nativo y WSL2 están en [NInfer en Windows](NINFER-WINDOWS.md).
