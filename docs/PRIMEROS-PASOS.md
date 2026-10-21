# Primeros pasos con Forja 50

Versión de desarrollo para Windows 11 x64, RTX 5070 Ti / RTX 5080 de escritorio (16 GB) o RTX 3090 (24 GB), con 32 GB de RAM. La prueba de inferencia de los nuevos perfiles en Forja está pendiente.

1. Instalá el Setup, o extraé el ZIP portable y ejecutá `app\Mi IA Local.exe`. No hace falta instalar CUDA ni compiladores.
2. Revisá la placa detectada. El modo nativo selecciona 64K y prefill 896 en la 5080, o 512 en la 5070 Ti. La 3090 selecciona KVMem para RTX 30, contexto 128K y prefill 512; su prueba física sigue pendiente. La configuración activa aparece en el panel izquierdo.
3. Elegí dónde guardar el modelo: Qwen de Todd (16,5 GB) en el modo nativo, o Swift (12,8 GB) en KVMem/3090. Reservá el espacio que indique el programa para preparar modelo, motor y herramientas.
4. Revisá las licencias y pulsá **Preparar IA**. Se descargan motor y modelo automáticamente. El descargador usa 16 conexiones y muestra GB, MB/s recientes y tiempo restante aproximado.
5. **Pausar** conserva los bytes y fragmentos guardados. **Reanudar preparación** continúa incluso después de cerrar. No borres el `.part` ni la carpeta `.chunks-*` mientras la descarga esté incompleta.
6. Elegí la carpeta de tu repositorio local con **Elegir proyecto**. No se copia tu repositorio.
7. Cuando quieras hacer la prueba, cerrá otras aplicaciones que usen mucha VRAM y pulsá **Iniciar IA**. La IA no arranca automáticamente al instalar, descargar o configurar.
8. **Detener** libera la memoria usada por la IA. Cerrar Forja detiene sus procesos propios y conserva los archivos y chats.

El Setup reúne carpeta de instalación, **Crear acceso directo en el escritorio** (marcado por defecto) y **Abrir Forja al terminar** en una sola pantalla. El acceso al menú Inicio se crea automáticamente. La carpeta del modelo se elige luego en Forja y no inicia ninguna descarga durante la instalación. El modo silencioso no abre la aplicación y respeta la selección de tareas indicada en sus argumentos.

## Configuración avanzada

Con la IA detenida, podés cambiar contexto, prefill, razonamiento, salida máxima, MTP, temperatura, CUDA Graph y presupuesto de visión. Prefill 0 recupera el valor automático por placa. La visión viene desactivada para dar prioridad al código. 128K es experimental en Windows nativo y puede no entrar en la memoria libre. Un valor numéricamente válido no garantiza que entre en VRAM.

La 3090 tiene opciones KVMem de 128K, 64K y 32K. Al cambiar desde los valores del perfil, se ajustan también historial y reserva para que quepan en el contexto elegido. Las personalizaciones incompatibles requieren corregirse en Configuración avanzada. **Restaurar recomendada** recupera los valores específicos de la placa. El motor sm120a de las RTX 50 permanece deshabilitado en la 3090.

## Si venís de una versión anterior

Las instalaciones anteriores conservan su selección KVMem/Swift. Elegí **NInfer nativo Windows · modelo oficial de Todd** para pasar al nuevo backend, revisá sus licencias y prepará la IA. Se preservan los archivos y la configuración anterior.

El modelo de Todd y Swift son archivos diferentes: un parcial de Swift no puede reutilizarse como parte del modelo de Todd. Forja conserva cada uno por separado. Los parciales del mismo modelo sí se pueden retomar desde versiones anteriores del descargador.

Si la descarga actual está trabada, pulsá Cancelar o Pausar y esperá a que se detenga antes de actualizar. Después usá **Buscar actualizaciones**. El actualizador verifica el paquete antes de abrir el instalador; en portable descarga el ZIP y explica cómo conservar `datos`.

## Datos y diagnóstico

La instalada usa `%LOCALAPPDATA%\MiIALocal`. La portable guarda `app\datos`. Conservá esa carpeta al mover o actualizar la portable; los modelos importados de una ubicación externa dependen de esa ruta.

## Administrar almacenamiento y desinstalar

Con la IA detenida, abrí **Administrar almacenamiento**. Verás el espacio y las rutas de modelos, componentes, caché/descargas incompletas, conversaciones y ajustes. Podés marcar categorías o usar **Seleccionar todo**; **Eliminar seleccionados** pide una confirmación con No como respuesta inicial. La eliminación es permanente. Borrar componentes permite descargarlos de nuevo; borrar parciales pierde ese progreso. Al borrar conversaciones o ajustes, Forja se cierra: volvé a abrirla para continuar.

Los archivos importados y sus carpetas se conservan. Se bloquea la limpieza de carpetas enlazadas, repositorios Git y la carpeta de proyecto seleccionada; también se conservan archivos desconocidos fuera de las carpetas administradas. Un modelo descargado en una ubicación personalizada aparece por su ruta exacta, sin borrar el resto de esa carpeta.

Al desinstalar con Setup, elegí si querés abrir esta misma limpieza antes de quitar el programa. **No** conserva los datos. **Sí** abre la selección y su confirmación; cerrar esa ventana conserva los datos restantes. La desinstalación silenciosa y las actualizaciones conservan los datos. La limpieza solo se abre si Forja no está ejecutándose.

**Diagnóstico y registros** abre los mensajes de la aplicación. Si falta WebView2 Runtime, instalalo desde la [página oficial de Microsoft](https://developer.microsoft.com/microsoft-edge/webview2/). Su preparación automática sigue pendiente.

El análisis de fuentes y la diferencia entre Windows nativo y WSL2 están en [NInfer en Windows](NINFER-WINDOWS.md).
