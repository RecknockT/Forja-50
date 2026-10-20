# Primeros pasos

Esta es una versión de desarrollo para Windows 11 y placas RTX 5070 Ti o RTX 5080 de escritorio de 16 GB. Necesita 32 GB de RAM y el controlador NVIDIA instalado. La 5070 Ti todavía requiere validación física.

1. Usá el Setup para instalar por usuario y crear el acceso directo, o extraé el ZIP portable y ejecutá `app\Mi IA Local.exe`. La portable guarda su configuración y conversaciones en `app\datos`; la instalada usa `%LOCALAPPDATA%\MiIALocal`.
2. Revisá el equipo detectado. Cerrá juegos u otras IA antes de cargar el modelo.
3. Elegí la ubicación del modelo. Para un modelo nuevo, reservá unos 30 GB de espacio libre durante la preparación. Si ya tenés exactamente el modelo compatible, usá **Usar modelo ya descargado**.
4. Revisá las licencias y pulsá **Preparar IA**. El modelo pesa unos 12,8 GB; además se descargan el motor y las herramientas. Si cancelás, podés reanudar.
5. Pulsá **Elegir proyecto** y seleccioná la carpeta de tu repositorio local. **64K** viene seleccionado y superó pruebas básicas en RTX 5080; uso prolongado y 5070 Ti siguen pendientes de validación. Podés elegir **32K** como alternativa.
6. Pulsá **Iniciar IA**. Aparece el chat dentro de la ventana. Ejemplo: “Leé las instrucciones del repositorio y explicame cómo ejecutar las pruebas”.
7. **Detener** libera la memoria usada por la IA. Al cerrar la aplicación también se detienen sus procesos. Los archivos y conversaciones se conservan.

Si falta Microsoft Edge WebView2 Runtime, esta versión indica el problema. Instalalo desde la [página oficial de Microsoft](https://developer.microsoft.com/microsoft-edge/webview2/). Su preparación automática en el instalador sigue pendiente.

El panel izquierdo muestra la configuración actual. Con la IA detenida, **Configuración avanzada** permite cambiar memoria de historial, caché RAM, salida, razonamiento, MTP, Ngram, formato KV y muestreo. **Restaurar recomendada** carga los valores de referencia de 64K; solo se guardan al pulsar **Guardar**. Más contexto lógico no equivale a conservar todos esos tokens simultáneamente en GPU. Los límites numéricos válidos tampoco garantizan que una combinación entre en VRAM.

Podés abrir los registros con **Diagnóstico y registros**. Conservá los mensajes del error para poder revisarlo. Desinstalar la versión instalada conserva sus datos. Para mover la portable, cerrá la IA primero y conservá también su carpeta `datos`; un modelo importado desde otra ubicación seguirá dependiendo de esa ruta externa.
