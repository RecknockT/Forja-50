# Mi IA Local — plan de producto

Fecha: 20 de octubre de 2026. Plan original conservado como referencia. El estado de implementación y pruebas está en ESTADO.md.

Decisiones posteriores: contexto predeterminado 64K con alternativa 32K; tema oscuro; panel de configuración y ajustes avanzados; publicación Setup y portable con datos locales a su carpeta. Ya existen paquetes de desarrollo y pruebas en RTX 5080. Las referencias inferiores a 32K como valor inicial describen el plan original y quedan reemplazadas por esta decisión.

## 1. Resultado que debe recibir una persona

Una aplicación de Windows con un solo acceso directo. La persona descarga el instalador, acepta los términos aplicables, elige dónde guardar el modelo y pulsa **Preparar mi IA**. El programa comprueba su equipo, descarga y verifica los componentes, configura el agente y realiza una prueba automática. Al terminar, permite elegir un repositorio y empezar a trabajar desde un chat.

La persona no escribe comandos, configura puertos, edita JSON, instala Node, busca un modelo ni abre terminales para iniciar o detener la IA. La instalación muestra el tamaño de la descarga y el espacio temporal necesario antes de empezar. Una descarga interrumpida puede reanudarse.

La experiencia reproduce lo que ya funciona en la PC de referencia: Qwen local en la RTX 5080, OpenCode como agente, lectura y edición de archivos, PowerShell, pruebas y Git local. La aplicación integra los controles y el chat en una sola ventana.

Nombre de trabajo: **Mi IA Local**. El nombre comercial y el icono definitivos pueden cambiar sin afectar la arquitectura.

## 2. Referencia funcional verificada

| Elemento | Configuración a reproducir |
|---|---|
| Plataforma inicial | Windows x64; Windows 11 como objetivo de la primera publicación, sujeto a pruebas en instalación limpia |
| GPU | NVIDIA RTX 5080, 16 GB de VRAM |
| RAM | 32 GB instalados; aproximadamente 31,2 GiB reportados por Windows |
| Driver probado | NVIDIA 617.42; no se presenta como mínimo universal |
| Modelo | Swift 1.5 basado en Qwen3.8-27B, GSQ-RCO IQ3_S, con MTP |
| Archivo | `swift15_iq3s_mtp.ninfer`, 12.790.639.360 bytes |
| Motor | KVMem 0.18.0 / NInfer, paquete RTX 50 sm120a |
| Agente | OpenCode 1.19.3 |
| Contexto | 32.768 tokens |
| Salida máxima | 8.192 tokens, incluido el razonamiento |
| Presupuesto de razonamiento | 2.048 tokens |
| KV | rk8v4; estado GDN en FP16 |
| Historial / reserva GPU | 24.576 / 8.192 tokens |
| Caché de historial en RAM | 2.048 MiB |
| Prefill | 256 tokens |
| Predicción especulativa | MTP, 3 borradores |
| Concurrencia | 1 solicitud; la aplicación debe coordinar también títulos y tareas auxiliares |
| Visión | Desactivada en el perfil inicial |
| Herramientas de referencia | Git 2.53.0, Node 24.19.0, npm 12.2.0 |

Pruebas ya realizadas en el prototipo: respuesta del modelo, chat gráfico, lectura y edición, PowerShell, creación de rama, comprobación de diff, commit y árbol limpio en un repositorio aislado. Se verificó iniciar, detener y reiniciar desde el bucle real de eventos del panel. Se comprobó que detener libera la memoria del modelo en la GPU.

Esto valida el punto de partida en esta máquina. No valida todavía un instalador público, otras GPUs, todas las variantes de Windows ni la calidad del modelo en tareas complejas.

## 3. Alcance de la primera versión

### Hardware obligatorio de lanzamiento

El alcance actual se limita exclusivamente a **PC de escritorio con RTX 5080 de 16 GB o RTX 5070 Ti de 16 GB**, en equipos con **32 GB de RAM**. Ambas son requisitos de la primera versión. Una sola descarga e instalador atiende estas dos placas mediante detección automática.

| Perfil | Punto de partida | Estado de verificación |
|---|---|---|
| RTX 5080 16 GB / RAM 32 GB | Configuración de esta PC, Qwen 27B IQ3_S y contexto 32K | Probado en el prototipo; falta validar el producto instalado en limpio |
| RTX 5070 Ti 16 GB / RAM 32 GB | Mismo modelo y motor, contexto inicial propuesto 32K; ajustes propios según mediciones | Hay pruebas comunitarias con NInfer/Swift en esta placa; la combinación exacta de este producto sigue pendiente de prueba física |

La detección debe distinguir el nombre exacto, VRAM disponible y variante de escritorio o portátil. No debe equiparar automáticamente RTX 5070, RTX 5070 Ti Laptop ni cualquier tarjeta de la misma arquitectura con estos dos perfiles. No se prometen los mismos tokens por segundo en ambas placas.

La [guía Windows de Ryan](https://github.com/Ryan-gsq/ninfer-16g-5070ti-5080-5090-qwen3.8-27b-gsq-rco/blob/main/docs/rtx-5070ti-windows.en.md) aporta evidencia externa sobre la 5070 Ti de 16 GB. Usa su propia combinación de motor y modelos, por lo que sus velocidades y contextos no se trasladan como resultados de nuestro programa. El criterio de salida exige que ambos perfiles superen las mismas pruebas de instalación, herramientas, Git y ciclo de vida en sus respectivas placas.

### Incluido

- Asistente de instalación y comprobación del equipo.
- Descarga automática y reanudable del modelo y componentes con versión fija.
- Verificación de integridad antes de instalar o cargar.
- Ventana en español con chat integrado, proyectos recientes y estado de la IA.
- Botón contextual **Iniciar IA / Detener IA** dentro de la aplicación.
- Selector de carpeta que abre el agente directamente en el proyecto correcto.
- Historial de conversaciones persistente.
- Lectura y edición, ejecución de pruebas y operaciones de Git local.
- Preferencias de trabajo e instrucciones del repositorio.
- Reparación de componentes, diagnóstico y actualización con recuperación de la versión anterior.
- Un solo acceso directo en escritorio y menú Inicio; ninguno adicional para detener.

### Fuera de la primera publicación

macOS, Linux, AMD, múltiples GPUs, entrenamiento, ejecución en red, acceso desde otros dispositivos, automatizaciones en segundo plano sin intervención, agentes paralelos y elección entre muchos modelos. Tampoco se instala automáticamente todo el entorno de desarrollo de cada repositorio: la aplicación detecta lo que falte y ofrece instrucciones pertinentes.

No se planifica soporte para otras placas ni notebooks en este alcance. El asistente debe identificar equipos fuera de estos dos modelos de escritorio y explicar la incompatibilidad antes de descargar el modelo, sin permitir activarlos como un perfil supuestamente compatible. Cualquier ampliación requerirá una nueva decisión del usuario.

## 4. Experiencia de instalación y uso

### Primera instalación

1. **Bienvenida:** qué hace la aplicación y qué componentes utiliza. Sin necesidad de una cuenta de nube para inferencia.
2. **Revisar mi equipo:** GPU, VRAM total y disponible, RAM disponible, Windows, driver, permisos y disco. Resultado claro: listo, necesita una acción o equipo fuera del perfil validado.
3. **Dónde guardar el modelo:** ruta sugerida y opción de otra unidad. Se muestra descarga pendiente, espacio final y espacio temporal estimado a partir del manifiesto.
4. **Preparar mi IA:** descarga, reanudación, comprobación, extracción y configuración. Cada etapa tiene progreso propio; el porcentaje no mezcla descarga con carga en GPU.
5. **Probar:** consulta sencilla al modelo y prueba de herramientas en una carpeta temporal creada por la aplicación. Los repositorios del usuario no intervienen en esta prueba.
6. **Listo:** elegir proyecto y abrir el chat. No se pide copiar URLs o claves.

El instalador por internet es la entrega principal. Descarga aproximadamente 12,8 GB del modelo más el motor y las herramientas. La carpeta de trabajo actual contiene unos 1,48 GB decimales de runtimes extraídos, además del modelo; es una medición de referencia, no el tamaño prometido del instalador final. Reservar inicialmente al menos 30 GB para instalación y temporales; el preflight calculará lo necesario según la distribución construida.

Un paquete completo sin conexión puede producirse después usando el mismo manifiesto. Tendrá varios GB de tamaño y deberá incluir también la interfaz web, el entorno de escritorio necesario y sus dependencias. No se lo debe anunciar como disponible hasta probarlo sin internet.

### Uso cotidiano

**Abrir aplicación → elegir proyecto → iniciar IA → escribir la tarea.**

El proyecto elegido se muestra siempre junto al chat. La interfaz indica la rama, la actividad de herramientas y los archivos modificados cuando el agente los informa. La revisión visual del diff precede a un commit cuando el usuario lo solicite.

Al detener, se cancela o termina ordenadamente la tarea según elección del usuario y se libera el modelo. Los chats y archivos permanecen guardados. Cerrar la ventana ofrece **Detener y salir** o **Continuar en segundo plano**; la primera opción es la predeterminada. Si continúa, aparece un icono de bandeja con el estado y la opción de detener. No se habilita inicio con Windows de forma predeterminada.

### Pantallas

| Pantalla | Contenido |
|---|---|
| Preparación | Compatibilidad, ubicación, descargas, comprobación y prueba |
| Principal | Proyecto actual, proyectos recientes, chat y control de la IA |
| Revisión | Cambios de la tarea y resultados de comandos/pruebas; puede reutilizar la vista de OpenCode |
| Ajustes | Ubicación del modelo, perfil probado, idioma, comportamiento al cerrar y actualizaciones |
| Ayuda | Estado real, explicación del error, reintento, reparación y exportación de diagnóstico |

La información técnica se reserva para Ajustes avanzados y Diagnóstico. La pantalla principal no presenta parámetros de CUDA, claves de API ni puertos.

## 5. Arquitectura propuesta

**Aplicación de escritorio en C#/.NET con WPF y chat integrado mediante WebView2.** Publicación autocontenida del runtime .NET para evitar que el usuario instale un SDK. El instalador detecta WebView2 Runtime y lo prepara cuando falte siguiendo el mecanismo oficial. No se presupone que tener Edge instalado sustituya a ese runtime.

Se conserva OpenCode como agente y NInfer/KVMem como motor, en procesos separados gestionados por la aplicación. La aplicación no reconstruye las herramientas de programación ni modifica los pesos del modelo. Esto mantiene cerca la combinación que ya pasó la prueba real.

```text
Mi IA Local.exe
  ├─ Asistente de preparación y gestor de descargas
  ├─ Proyectos, configuración y diagnóstico
  ├─ Supervisor de procesos y estado de GPU
  └─ Chat integrado de OpenCode
          ├─ Herramientas sobre el proyecto elegido
          │     └─ Archivos, PowerShell, pruebas y Git
          └─ API privada local → NInfer/KVMem → Qwen → RTX 5080 / RTX 5070 Ti
```

El motor conserva inicialmente los argumentos del perfil de referencia para la 5080. La 5070 Ti recibe un perfil independiente y se ajusta según pruebas, preservando la misma experiencia. Las rutas y puertos se calculan en tiempo de ejecución. Un conflicto de puerto se resuelve eligiendo otro y propagándolo al agente y a la interfaz; nunca cerrando programas ajenos.

El chat se incorpora desde la versión de interfaz correspondiente al agente. Sus recursos deben empaquetarse localmente para una experiencia independiente de un sitio externo. Durante la fase técnica se verificará la forma de distribuir esa interfaz, sus llamadas auxiliares y sus condiciones. El navegador predeterminado queda como mecanismo de recuperación, no como flujo principal.

Los binarios para distribución se obtienen de fuentes oficiales o se construyen de fuentes fijadas, con sus licencias. No se distribuyen copias extraídas de las carpetas internas de otras aplicaciones instaladas en esta PC.

### Componentes de código

```text
src/
  MiIALocal.Desktop/       Ventanas WPF, accesibilidad, WebView2 y bandeja
  MiIALocal.Core/          Estados, perfiles, proyectos y configuración
  MiIALocal.Runtime/       Procesos, salud, parada y recuperación
  MiIALocal.Provisioning/  Descargas, integridad y extracción segura
  MiIALocal.Diagnostics/  Registros y pruebas de funcionamiento
installer/                Instalador por usuario y desinstalación
profiles/                 Manifiestos de combinaciones probadas
tests/                    Pruebas de integración y regresiones
licenses/                 Avisos, licencias y procedencia
docs/                     Guía de uso y publicación
```

El nombre final del instalador y la herramienta de empaquetado se fijan en la fase técnica, comparando instalación por usuario, reparación, actualización y firma. La elección no debe exigir privilegios de administrador para el uso diario.

## 6. Datos, procesos y permisos

### Almacenamiento

- Programa: `%LOCALAPPDATA%\Programs\MiIALocal\`.
- Configuración e historial: `%LOCALAPPDATA%\MiIALocal\`.
- Modelos: ubicación elegida; predeterminado bajo los datos de la aplicación.
- Temporales y descargas parciales: subcarpeta dedicada y recuperable.
- Proyectos: sus carpetas originales; no se copian dentro de la aplicación.

Las rutas personales, repositorios del usuario, historial de pruebas, credenciales, archivos personales, puertos de esta instalación y PIDs nunca forman parte del paquete público. El perfil distribuible contiene versiones y ajustes, no datos de esta PC.

### Supervisor

Estados explícitos: **Sin preparar → Descargando → Verificando → Detenida → Cargando → Lista → Deteniendo**. Los errores tienen código propio, descripción comprensible y acción concreta. Cancelar y reintentar no deja el sistema confundido entre estados.

- Una sola operación de arranque o parada a la vez.
- Instancia única de la aplicación por usuario.
- Procesos identificados por handle, identidad y momento de creación; no por nombres genéricos.
- Captura fiable del código de salida y salida de registros.
- Salud del motor y del agente comprobadas por separado.
- Detener la aplicación no termina instalaciones ajenas de OpenCode, Git o modelos.
- Cancelación ordenada; terminación forzada solo como recuperación visible de los procesos propios.
- Registros con UTF-8 explícito en toda la cadena.
- Bucle de interfaz asíncrono, sin bloquear ni reentrar durante avisos.
- Al iniciar tras un cierre inesperado, comprobar procesos propios y recuperarlos o cerrarlos de forma controlada.

### Acceso al proyecto

El usuario elige una carpeta y ve que el agente puede modificarla y ejecutar comandos. Se respetan AGENTS.md y demás instrucciones del repositorio. Se conserva la preferencia de no incluir “codex” en nombres de ramas.

Lectura, edición, pruebas y Git local están disponibles. Acciones como publicar o hacer push requieren la instrucción correspondiente. Las aprobaciones se basan en las operaciones efectivas: no se presenta una simple lista de prefijos de comandos como un aislamiento de seguridad. El alcance de acceso y la ejecución de comandos se documentan con claridad.

Servicios enlazados solo a loopback, autenticación interna con secretos efímeros y sin exponerlos en URLs. La aplicación no extrae ni reenvía credenciales del usuario. Git remoto utiliza la autenticación que el usuario tenga configurada o le solicita configurarla cuando corresponda.

## 7. Instalador, reparación y actualizaciones

Cada publicación incluye un manifiesto que fija motor, agente, interfaz, modelo, herramientas, tamaño de cada archivo, hash y origen. No se descarga “latest” durante una instalación de una versión publicada.

La descarga utiliza archivos parciales, reintentos con límites, validación y traslado atómico al destino. La extracción valida que ninguna entrada del archivo comprimido salga de la carpeta prevista. Cancelar conserva lo útil para reanudar. Un archivo con hash incorrecto no se ejecuta ni se carga.

**Reparar instalación** revisa los archivos e identifica cuál falta o está dañado. Descarga solo lo necesario. No borra conversaciones ni vuelve a descargar un modelo correcto.

Las actualizaciones se instalan con el motor detenido, en una carpeta de versión nueva. Se guarda la combinación anterior hasta superar la prueba de arranque. Las migraciones de configuración e historial tienen copia de seguridad y camino de recuperación. Actualizar la aplicación no cambia silenciosamente el modelo o su licencia.

La desinstalación cierra exclusivamente los procesos propios y retira el programa y su acceso directo. Pregunta por separado si se quieren conservar o eliminar modelos e historial; conservar es la opción predeterminada. Nunca elimina repositorios del usuario.

La publicación debe disponer de instalador y binarios firmados cuando se habilite la distribución pública. Un certificado y su reputación requieren preparación externa; la planificación no los da por adquiridos ni promete que Windows nunca muestre avisos.

## 8. Fases de desarrollo y condiciones de avance

| Fase | Entrega concreta | Se considera terminada cuando… |
|---|---|---|
| 0. Congelar referencia | Manifiesto, hashes, argumentos y prueba reproducible | Se reproduce la configuración actual sin depender de rutas del usuario |
| 1. Validar integración | WPF autocontenido + WebView2 + motor/agente en perfiles aislados | Se abre el chat integrado y completa la prueba de herramientas |
| 2. Construir aplicación | Estados, proyectos, iniciar/detener, historial, errores y bandeja | Una persona usa el programa sin terminales ni archivos de configuración |
| 3. Preparación automática | Preflight, descarga reanudable, verificación y reparación | Una máquina sin los componentes pasa del instalador al primer chat |
| 4. Empaquetar | Instalación por usuario, desinstalación y actualización con recuperación | Instalar, actualizar y desinstalar conserva datos según la elección |
| 5. Probar publicación | Informe de instalación limpia y pruebas en 5080 y 5070 Ti | Ambas placas pasan la matriz obligatoria y las licencias están resueltas |
| 6. Publicar | Descarga identificable, notas, hashes, guía y soporte | Existe una entrega firmada, comprobable y reproducible |

Las estimaciones de tiempo se realizan después de la fase 1, cuando estén comprobados el empaquetado de la interfaz, WebView2 y la compatibilidad del motor. No se promete una fecha basada solo en la existencia del prototipo.

## 9. Pruebas obligatorias de la versión inicial

1. **Instalación limpia:** Windows objetivo sin Python, Node, Git, SDK de .NET ni CUDA Toolkit instalados por separado. Probar con WebView2 presente y ausente.
2. **Hardware obligatorio:** ejecutar la matriz tanto en RTX 5080 de 16 GB como en RTX 5070 Ti de escritorio de 16 GB, ambas con 32 GB de RAM; probar también con VRAM ocupada por otras aplicaciones. La aplicación informa falta de memoria y permite reintentar. La selección del perfil no cambia de modelo silenciosamente. No se publica el soporte de una placa con resultados de la otra.
3. **Preparación:** interrupción de red, reanudación, cancelación, disco lleno, archivo incompleto y hash incorrecto. No se ejecuta una instalación parcial.
4. **Rutas:** usuario y carpetas con espacios y caracteres no ASCII, modelos en otra unidad y cambio de ubicación. Validar el soporte real del motor; cualquier limitación se resuelve en el asistente antes de publicar.
5. **Agente real:** repositorio temporal, lectura, edición, PowerShell, prueba simple, creación de rama, diff y commit con un único archivo previsto. Confirmar el resultado por Git, no solo por el mensaje del modelo.
6. **Estado previo:** repositorio con cambios del usuario, archivos sin seguimiento y reglas propias. La prueba no los incorpora ni los descarta.
7. **Ciclo de vida:** doble clic repetido, iniciar/detener durante una carga, cerrar ventana, segundo plano y recuperación después de un cierre inesperado.
8. **Regresión del prototipo:** proceso que termina con código 0 no genera error; error antiguo no se reutiliza; aviso no reentra al temporizador; ningún acceso a objetos nulos; caracteres del español correctos.
9. **Procesos ajenos:** OpenCode o servidor de modelos iniciado por otra aplicación no es detenido. Puertos ocupados se resuelven sin matar procesos ajenos.
10. **Historial:** reinicio y actualización conservan chats y proyecto correcto. El selector y el chat muestran la misma carpeta.
11. **Privacidad y red:** comprobar destinos de red, autenticación local y exportación de diagnóstico sin contenidos privados de conversaciones o proyectos por defecto.
12. **Sin conexión:** comprobar el funcionamiento con recursos web locales y red desconectada antes de anunciar esa capacidad.
13. **Accesibilidad:** navegación por teclado, lector de pantalla, contraste y escalado 100/150/200%; estados y errores no dependen solo de colores.
14. **Desinstalación y reparación:** conservar datos funciona; borrar modelos es una elección explícita; ningún repositorio se altera.

La publicación se bloquea ante pérdida de datos, selección incorrecta del proyecto, detención de procesos ajenos, ejecución de descargas sin verificar o un fallo en la prueba de herramientas. La primera versión solicitada no se considera lista hasta superar la validación tanto en 5080 como en 5070 Ti.

## 10. Distribución y licencias

Se debe conservar una matriz de procedencia y obligaciones de cada componente: aplicación propia, OpenCode y su interfaz, NInfer/KVMem y sus DLLs, modelo Swift y base Qwen, Git, Node/npm, .NET y WebView2.

El archivo LICENSE del modelo que está instalado es **Swift Open License v1.0**. Incluye condiciones específicas para uso comercial y un umbral de ingresos definido por la propia licencia; no se debe presentar este modelo como Apache-2.0 sin restricciones adicionales. El paquete incorpora también LICENSE-APACHE-2.0 y NOTICE. Antes de distribuir comercialmente, se debe resolver el cumplimiento de esos términos o elegir otro modelo y repetir las pruebas. Cambiarlo implicaría dejar de reproducir exactamente la combinación actual.

Las condiciones de Git y demás dependencias también deben acompañar su redistribución, con fuentes o mecanismos exigidos cuando correspondan. El instalador no redistribuye las credenciales, cuentas o licencias de aplicaciones instaladas en esta PC. No se solicita al usuario aceptar términos hasta que el instalador muestre el contenido aplicable.

## 11. Definición de terminado

Una persona en una PC compatible y limpia descarga el instalador; completa el asistente sin terminales, configuraciones manuales ni ayuda externa; elige un repositorio; pide un cambio y revisa su diff; solicita un commit y confirma que se creó correctamente; detiene la IA y recupera la memoria de GPU; vuelve al día siguiente y encuentra su historial.

La entrega incluye: instalador, aplicación, manifiesto de componentes, notas de versión, hashes, licencias, guía breve y resultados reproducibles de las pruebas. Hasta superar estas condiciones debe llamarse prototipo o versión de prueba, no producto listo para cualquier PC.

## Fuentes y referencia

- Instalación funcional y registros locales de IA-Local, verificados el 20/10/2026.
- [Motor KVMem 0.18.0](https://github.com/kvmem/kvmem/releases/tag/v0.18.0).
- [Modelo y archivos de licencia](https://huggingface.co/fyb1214/Swift-1.5-Qwen3.8-27B-GSQ-RCO-NInfer/tree/7da3e38b9ee7a6c8888cf2112d3c5a48a952a2a8).
- [OpenCode, versión de referencia y licencia](https://github.com/anomalyco/opencode/tree/v1.19.3).
- [Aplicaciones WPF](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/).
- [Distribución del runtime WebView2](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution).
- [Especificaciones oficiales RTX 5070 Ti](https://www.nvidia.com/en-us/geforce/graphics-cards/50-series/rtx-5070-family/).
- [Pruebas comunitarias de NInfer en Windows con 5070 Ti](https://github.com/Ryan-gsq/ninfer-16g-5070ti-5080-5090-qwen3.8-27b-gsq-rco/blob/main/docs/rtx-5070ti-windows.en.md).
