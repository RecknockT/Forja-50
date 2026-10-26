# Primeros pasos con Forja 50

Versión de desarrollo para Windows 11 x64, RTX 5070 Ti / RTX 5080 de escritorio (16 GB) o RTX 3090 (24 GB), con 32 GB de RAM. La prueba de inferencia de los nuevos perfiles en Forja está pendiente.

1. Instalá el Setup, o extraé el ZIP portable y ejecutá `app\Forja 50.exe`. No hace falta instalar CUDA ni compiladores.
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

## Imágenes y animación (0.4.0 experimental)

Elegí **Imágenes y animación** en Modo de uso. No hace falta elegir un proyecto ni instalar Qwen de programación, OpenCode o Git. Desde 0.4.19 hay un único generador: **HiDream-O1 Full FP8 escalado** (8,07 GB), compartido por Crear imagen y Editar imagen en las tres placas. HiDream, Gemma y Wan vienen marcados por defecto. Revisá las licencias y marcá Revisé y acepto: Descargar y preparar se habilitará en verde. Cuando termine todo lo seleccionado, la sección se replegará y podrás usar Iniciar IA, que también se destaca cuando el equipo y el perfil están listos. La descarga es directa, sin cuenta ni token de Hugging Face.

El único asistente visual es **Gemma 4 12B INT8** (12,06 GB). Podés desactivarlo y escribir directamente la descripción, preferentemente en inglés. Cuando está activado, analiza el pedido y la referencia antes de liberar la GPU para HiDream. Con **Revisar y editar la descripción preparada** podés corregir el texto, pedir otra versión, volver al original o cancelar. La generación usa el texto que confirmaste; el asistente no se ejecuta otra vez.

Crear y editar usan el mismo Full: **40 pasos, CFG 5, DPM++ 2M SDE y suavizado de costuras**. Si elegís una referencia, se conserva su encuadre en una resolución entrenada cercana a 4 MP. Una referencia cercana del personaje suele ser más útil que una portada o un paisaje amplio. Las resoluciones inferiores a 2048 son opciones de prueba y pueden perder fidelidad.

Para **Animar**, elegí una imagen inicial y describí su movimiento. Usá **Automático** para adaptar el lienzo a sus proporciones sin franjas de relleno; los formatos fijos recortan el encuadre. La opción **Más detalle** aumenta la resolución y es experimental: empezá con 3 segundos y comprobá memoria y tiempo. El tamaño real aparece debajo de los controles. Se conserva Wan A14B: NVFP4 normal/Sol experimental en RTX 50 y GGUF en 3090. El asistente puede preparar también la descripción del movimiento. El panel muestra la descarga exacta según tu placa y las opciones; lo descargado se reutiliza y los parciales se conservan al pausar.

Dev FP8/BF16 y Gemma E4B quedan en **Modelos anteriores**, con borrado individual en Administrar almacenamiento. Tus creaciones se conservan. La prueba física del perfil unificado está pendiente; no hay tiempos ni consumo garantizados. [Configuración y plan de pruebas](HIDREAM-MIGRACION.md).

## Ubicación de los datos

Desde 0.2.4 las instalaciones nuevas usan `%LOCALAPPDATA%\Programs\Forja 50` para el programa y `%LOCALAPPDATA%\Forja 50` para los datos (modelos en `models`). Al actualizar se reemplaza la carpeta predeterminada anterior por `Forja 50`; las ubicaciones personalizadas se conservan. Se reutilizan los datos anteriores en `%LOCALAPPDATA%\MiIALocal` si existen; no se mueven ni se vuelven a descargar. La portable guarda `app\datos`. Conservá esa carpeta al mover o actualizar la portable; los modelos importados de una ubicación externa dependen de esa ruta.

## Administrar almacenamiento y desinstalar

Con la IA detenida, abrí **Administrar almacenamiento**. Verás el espacio y las rutas de modelos, componentes, caché/descargas incompletas, conversaciones, ajustes y sesión de GitHub. Podés marcar categorías o usar **Seleccionar todo**; **Eliminar seleccionados** pide una confirmación con No como respuesta inicial. La eliminación es permanente. Borrar componentes permite descargarlos de nuevo; borrar parciales pierde ese progreso. Al borrar conversaciones o ajustes, Forja libera el navegador y reinicia la vista sin cerrar la aplicación. Si borraste ajustes, muestra los valores iniciales para preparar de nuevo la IA.

## Cambio de nombre de los paquetes

Desde 0.2.3 los paquetes se llaman `Forja-50-VERSION-Setup.exe` y `Forja-50-VERSION-Portable-win-x64.zip`, en el repositorio `RecknockT/Forja-50`. La aplicación conserva sus identificadores y AppId, y detecta la carpeta de datos anterior para mantener instalaciones existentes. El nuevo actualizador reconoce ambos nombres de paquetes y los dos nombres conocidos del repositorio.

Las versiones hasta 0.2.2 validaban de forma estricta la URL antigua; tras renombrar el repositorio pueden no detectar esta actualización. En ese caso instalá manualmente 0.2.3 una vez desde la publicación oficial. No hace falta borrar tus datos para actualizar.

Los archivos importados y sus carpetas se conservan. Se bloquea la limpieza de carpetas enlazadas, repositorios Git y la carpeta de proyecto seleccionada; también se conservan archivos desconocidos fuera de las carpetas administradas. Un modelo descargado en una ubicación personalizada aparece por su ruta exacta, sin borrar el resto de esa carpeta.

Al desinstalar con Setup, elegí si querés abrir esta misma limpieza antes de quitar el programa. **No** conserva los datos. **Sí** abre la selección y su confirmación; cerrar esa ventana conserva los datos restantes. La desinstalación silenciosa y las actualizaciones conservan los datos. La limpieza solo se abre si Forja no está ejecutándose.

**Diagnóstico y registros** abre los mensajes de la aplicación. Si falta WebView2 Runtime, instalalo desde la [página oficial de Microsoft](https://developer.microsoft.com/microsoft-edge/webview2/). Su preparación automática sigue pendiente.

El análisis de fuentes y la diferencia entre Windows nativo y WSL2 están en [NInfer en Windows](NINFER-WINDOWS.md).


## Correcciones 0.4.1

El asistente visual se carga en la GPU seleccionada aun con ahorro de memoria. Si no completa la preparación en cinco minutos, se detiene e informa cómo continuar; podés desactivar el asistente y usar tu descripción. El texto preparado se puede revisar antes de generar. Detener libera el proceso y sus modelos. La velocidad de esta corrección todavía requiere una nueva medición física.

Podés cerrar Administrar almacenamiento mientras calcula el espacio; eso cancela el cálculo y conserva los datos. Durante una eliminación confirmada, esperá a que termine. La desinstalación silenciosa conserva datos por diseño.

En Administrar almacenamiento, los modelos de programación y los de imágenes/animación aparecen en apartados separados. Cada modelo tiene una casilla propia: borrar uno no selecciona los demás. La confirmación enumera exactamente los elementos elegidos.

### Referencias con Gemma

En Crear imagen, activá el asistente y Buscar referencias en Wikipedia con Gemma. Escribí el pedido con naturalidad: por ejemplo «un bosque de noche con Frieren dibujada como anime de los 90». Gemma identifica el personaje y propone la consulta; Forja busca en Wikipedia, Gemma ordena las candidatas por título/dimensiones y después elegís la imagen. El estilo se conserva para generar; no se busca una serie concreta por pedir una estética general.

Podés forzar el nombre en Opciones de la referencia, elegir una imagen de tu PC o crear sin referencia. Editar y animar conservan la imagen elegida. La búsqueda agrega análisis local y requiere internet para Wikipedia; no da a Gemma navegación general ni envía imágenes locales. Si desactivás el asistente, la búsqueda vuelve a la detección simple de nombres.
