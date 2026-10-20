# Mi IA Local

Aplicación de Windows 11 para trabajar con archivos y Git usando Qwen local. Alcance inicial: RTX 5070 Ti y RTX 5080 de escritorio, ambas de 16 GB, con 32 GB de RAM.

Versión de desarrollo 0.1.2. Usa WPF, WebView2, NInfer/KVMem y OpenCode. El modelo de 12,8 GB se descarga después de instalar; no forma parte del paquete. Los componentes se descargan desde versiones fijadas y se verifican con SHA-256.

## Desarrollo

Desde PowerShell, en esta carpeta:

```powershell
.\scripts\Preparar-Desarrollo.ps1
.\scripts\Compilar.ps1
.\scripts\Abrir-Desarrollo.ps1
```

El SDK se guarda en `.tools`, sin cambiar el SDK global. `Abrir-Desarrollo.ps1` guarda los datos en `.dev-data`, separados de la instalación anterior. La versión instalada usa `%LOCALAPPDATA%\MiIALocal`. La portable incluye `portable.flag` y guarda sus datos en `datos` junto al ejecutable; `--data-root` permite elegir otra ubicación.

Para generar un ZIP autocontenido, sin necesitar .NET instalado en el equipo destino:

```powershell
.\scripts\Compilar.ps1 -Publicar
```

Para generar además el instalador por usuario, indicar la ruta real de `ISCC.exe` de Inno Setup 6 mediante `-CompiladorInstalador`. El script imprime la carpeta de salida y el hash del ZIP. No incluye pesos, cachés, conversaciones ni datos de desarrollo.

Para preparar el compilador oficial dentro del proyecto y producir ambos formatos:

```powershell
.\scripts\Preparar-Instalador.ps1
.\scripts\Compilar.ps1 -Publicar -CompiladorInstalador "$PWD\.tools\inno\ISCC.exe"
```

## Compilaciones en GitHub

El workflow [Compilar Windows](https://github.com/RecknockT/Mi-IA-Local/actions/workflows/windows.yml) compila y prueba en Windows, y genera Setup, ZIP portable y un manifiesto con SHA-256. Se ejecuta al subir cambios a `main`, en pull requests y desde **Run workflow**. Las descargas aparecen como artifacts en cada ejecución durante 30 días. El runner no necesita una GPU y no descarga el modelo; sus pruebas no sustituyen la validación física.

Al subir una etiqueta `v` seguida de la versión declarada en `ReleaseProfile.Version`, por ejemplo `v0.1.2-dev`, se recompila y se publican los archivos en una prerelease de [Releases](https://github.com/RecknockT/Mi-IA-Local/releases). La etiqueta debe coincidir con la versión del código. La publicación de la prerelease ocurre solo si la compilación y las pruebas pasan. Se conserva la visibilidad del repositorio; en un repositorio privado las descargas requieren acceso.

## Estado

- Interfaz nativa en español, selección de repositorio, importación de modelo, descarga reanudable, inicio y detención de procesos propios.
- Chat integrado con archivos, terminal y Git del proyecto. Motor y agente escuchan solo en loopback con autenticación efímera.
- Contexto predeterminado 64K, caché KV en RAM 4 GiB y alternativa 32K. Historial GPU 24K + reserva 8K en ambos perfiles. Pruebas básicas de recuperación aprobadas en RTX 5080 con entradas de 43K y 52K; pendiente uso prolongado y prueba física en 5070 Ti.
- Tema oscuro en ventana y chat, panel con configuración activa y ajustes avanzados con validación y restauración de recomendados. Incluye presupuesto KV, salida, razonamiento, MTP, Ngram y muestreo. Los cambios se realizan con la IA detenida.
- Pruebas automatizadas sobre hardware, rutas ZIP, descargas, configuración, Unicode y procesos. Ver `docs/ESTADO.md` para límites de validación.
- Setup comprobado en esta PC: instalación, arranque y desinstalación. ZIP portable comprobado con datos locales y 64K. Windows limpio, sesiones prolongadas y 5070 Ti física siguen pendientes antes de distribuirlo como versión final.

## Licencias

Las condiciones del modelo Swift y sus avisos están en `licenses/model`. No dar por hecho que todo uso comercial está permitido sin condiciones. Los motores y herramientas conservan sus licencias de origen. La licencia pública de este proyecto todavía está pendiente de elección.
