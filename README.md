![Forja 50](src/MiIALocal.Desktop/Assets/logo.png)

# Forja 50

Aplicación de Windows 11 para trabajar con archivos, terminal y Git usando IA local. Solo RTX 5070 Ti y RTX 5080 de escritorio, 16 GB de VRAM y 32 GB de RAM.

La versión 0.2.1 de desarrollo incorpora el port Windows/MSVC de YukinoKaorisuna sobre ninfer-5080 de Todd Ballinger y su modelo oficial Qwen3.8-27B. El instalador y el portable no contienen pesos ni compiladores: el programa descarga el motor preparado, herramientas y modelo después de instalar.

**Administrar almacenamiento** permite eliminar modelos, componentes, caché, conversaciones y ajustes, mostrando rutas y espacio y pidiendo confirmación. El desinstalador ofrece la misma limpieza opcional; conservar datos es la opción inicial. Los archivos importados, los proyectos y los repositorios Git quedan protegidos. Ver [limpieza y desinstalación](docs/PRIMEROS-PASOS.md#administrar-almacenamiento-y-desinstalar).

## Perfiles por placa

| Ajuste inicial | RTX 5080 | RTX 5070 Ti |
| --- | --- | --- |
| Contexto / KV completos | 65.536 | 65.536 |
| Prefill automático | 896 | 512 |
| Caché KV | Q4 | Q4 |
| MTP | 3 borradores | 3 borradores |
| Embeddings | En RAM | En RAM |
| Visión inicial | Desactivada | Desactivada |
| Prueba de inferencia de este perfil en Forja | Pendiente | Pendiente |

Los parámetros son puntos de partida para probar, no una afirmación de rendimiento óptimo. Los ajustes manuales de configuración avanzada se conservan. 128K queda marcado como experimental en Windows nativo. El backend anterior KVMem/Swift permanece como alternativa y conserva sus archivos.

El [análisis de experiencias en Windows](docs/NINFER-WINDOWS.md) documenta fuentes, versiones, recetas, resultados de terceros y límites de validación. Los resultados de WSL2 no se atribuyen al port nativo.

## Descarga a máxima velocidad disponible

Dieciséis conexiones con trabajadores continuos y fragmentos de 64 MiB; una conexión rápida no espera a las demás. No se impone un límite de MB/s. La red, el servidor y el disco determinan la velocidad real. El indicador muestra transferencia reciente, no el promedio de todas las esperas desde el inicio.

Pausar conserva el prefijo y los fragmentos completos, incompletos o fuera de orden. Reanudar continúa desde los bytes guardados. Los parciales anteriores son compatibles si pertenecen al mismo modelo. Cada descarga final se verifica con tamaño y SHA-256. Hay timeout de 30 segundos sin respuesta/datos, hasta tres reintentos y fallback a una conexión si el servidor no respeta Range.

## Instalar y usar

Seguí [Primeros pasos](docs/PRIMEROS-PASOS.md). El programa detecta la placa, permite elegir la carpeta del modelo y trabajar con un repositorio Git existente. El chat usa OpenCode y se comunica con el motor solo por loopback con claves efímeras.

La instalación guarda datos en `%LOCALAPPDATA%\MiIALocal`; el portable usa `app\datos` junto al ejecutable. `--data-root` permite datos aislados. Se mantienen los nombres internos Mi-IA-Local, el AppId y los nombres de paquetes para actualizar instalaciones anteriores.

## Desarrollar y generar paquetes

```powershell
.\scripts\Preparar-Desarrollo.ps1
.\scripts\Compilar.ps1
.\scripts\Abrir-Desarrollo.ps1
```

El SDK queda en `.tools`; las pruebas no descargan el modelo ni cargan la GPU. `Abrir-Desarrollo.ps1` usa `.dev-data`. Para Setup y portable autocontenidos:

```powershell
.\scripts\Preparar-Instalador.ps1
.\scripts\Compilar.ps1 -Publicar -CompiladorInstalador "$PWD\.tools\inno\ISCC.exe"
```

[Compilar Windows](https://github.com/RecknockT/Mi-IA-Local/actions/workflows/windows.yml) ejecuta las comprobaciones en GitHub. Una etiqueta coincidente con `ReleaseProfile.Version`, por ejemplo `v0.2.1-dev`, genera una prerelease con Setup, portable y manifiesto de integridad.

El [workflow del motor nativo](.github/workflows/native-engine.yml) compila la revisión inmutable y las herramientas declaradas en `build/ninfer-windows.lock.json`. Usa componentes CUDA portables con hashes oficiales y MSVC 14.44 en un runner Windows; no instala controladores en la PC del usuario. Comprobar `--help` no equivale a validar inferencia.

## Estado y licencias

La validación de la descarga, configuración, rutas, cierre, pausa y migración se automatiza. Windows limpio, uso prolongado y pruebas físicas del nuevo backend en ambas placas siguen pendientes. Los paquetes son de desarrollo y no tienen firma de editor.

El [modelo oficial de Todd](https://huggingface.co/ninfer-5080/Qwen3.8-27B-RTX5080) declara Apache-2.0. Los avisos del motor, sus dependencias y CUDA están en `licenses/ninfer-native`. El modelo Swift de compatibilidad conserva sus propias condiciones en `licenses/model`. La licencia pública del código propio de Forja todavía está pendiente de elección.
