# Primeros pasos

Esta es una versión de desarrollo para Windows 11 y placas RTX 5070 Ti o RTX 5080 de escritorio de 16 GB. Necesita 32 GB de RAM y el controlador NVIDIA instalado. La 5070 Ti todavía requiere validación física.

1. Usá el Setup para instalar por usuario y crear el acceso directo, o extraé el ZIP portable y ejecutá `app\Mi IA Local.exe`. La portable guarda su configuración y conversaciones en `app\datos`; la instalada usa `%LOCALAPPDATA%\MiIALocal`.
2. Revisá el equipo detectado. Cerrá juegos u otras IA antes de cargar el modelo.
   El programa selecciona el perfil de la 5080 o de la 5070 Ti automáticamente. Hoy ambos parten de 64K; la prueba física de la 5070 Ti sigue pendiente. Los cambios manuales en configuración avanzada se conservan entre inicios.
3. Elegí la ubicación del modelo. Para un modelo nuevo, reservá unos 30 GB de espacio libre durante la preparación. Si ya tenés exactamente el modelo compatible, usá **Usar modelo ya descargado**.
4. Revisá las licencias y pulsá **Preparar IA**. El modelo pesa unos 12,8 GB; además se descargan el motor y las herramientas. Si cancelás, podés reanudar.
5. Pulsá **Elegir proyecto** y seleccioná la carpeta de tu repositorio local. **64K** viene seleccionado y superó pruebas básicas en RTX 5080; uso prolongado y 5070 Ti siguen pendientes de validación. Podés elegir **32K** como alternativa.
6. Pulsá **Iniciar IA**. Aparece el chat dentro de la ventana. Ejemplo: “Leé las instrucciones del repositorio y explicame cómo ejecutar las pruebas”.
7. **Detener** libera la memoria usada por la IA. Al cerrar la aplicación también se detienen sus procesos. Los archivos y conversaciones se conservan.

Si falta Microsoft Edge WebView2 Runtime, esta versión indica el problema. Instalalo desde la [página oficial de Microsoft](https://developer.microsoft.com/microsoft-edge/webview2/). Su preparación automática en el instalador sigue pendiente.

El panel izquierdo muestra la configuración actual. Con la IA detenida, **Configuración avanzada** permite cambiar memoria de historial, caché RAM, salida, razonamiento, MTP, Ngram, formato KV y muestreo. **Restaurar recomendada** carga los valores de referencia de 64K; solo se guardan al pulsar **Guardar**. Más contexto lógico no equivale a conservar todos esos tokens simultáneamente en GPU. Los límites numéricos válidos tampoco garantizan que una combinación entre en VRAM.

Podés abrir los registros con **Diagnóstico y registros**. Conservá los mensajes del error para poder revisarlo. Desinstalar la versión instalada conserva sus datos. Para mover la portable, cerrá la IA primero y conservá también su carpeta `datos`; un modelo importado desde otra ubicación seguirá dependiendo de esa ruta externa.

## Actualizaciones
Usá Buscar actualizaciones en el panel izquierdo. El programa consulta Releases de GitHub y verifica tamaño y SHA-256 antes de usar el paquete. Las versiones de desarrollo siguen también las publicaciones preliminares. La instalación conserva modelos, chats y ajustes. El modelo y los motores solo cambian al preparar los componentes de una nueva versión; no se actualizan durante un chat.

En Setup, al aceptar la actualización se descarga, se detiene la IA, se cierra la aplicación y se abre el instalador en la ubicación actual. En portable se descarga el ZIP: cerrá el programa, extraé en una carpeta nueva y copiá la carpeta datos anterior. Conservá el original hasta comprobar el funcionamiento. El botón Versiones en GitHub permite consultar notas o descargar manualmente si GitHub no responde o requiere iniciar sesión.

## Pausar y reanudar
Durante la preparación usá Pausar. Al terminar de guardar el progreso aparecerá Reanudar preparación. Podés cerrar y volver a abrir sin borrar lo descargado. Se conservan bloques completos, y puede repetirse hasta el último lote de 32 MiB. La descarga muestra MB/s y tiempo restante aproximado; una conexión sin datos durante 30 segundos se reintenta automáticamente hasta tres veces.

Si venís de 0.1.5 o anterior y la descarga quedó trabada: pulsá Cancelar, esperá a que se detenga, buscá actualizaciones e instalá esta versión. Después pulsá Reanudar preparación. No borres el archivo .part ni cambies la ubicación del modelo durante ese proceso.
