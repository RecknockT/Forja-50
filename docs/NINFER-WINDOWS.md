# NInfer-5080 en Windows: fuentes, alcance y configuración

Revisión de fuentes realizada el 21 de octubre de 2026. Este documento distingue los resultados publicados por terceros de las comprobaciones realizadas para Forja 50. La prueba de inferencia local de este nuevo motor queda pendiente por indicación del usuario.

## Qué se encontró

| Fuente | Entorno y método | Evidencia publicada | Límites |
| --- | --- | --- | --- |
| [YukinoKaorisuna, PR #16](https://github.com/toddballinger/ninfer-5080/pull/16) | Port nativo MSVC/CUDA sobre el fork de Todd; probado en una RTX 5070 Ti | Compilación completa, inferencia y API HTTP. La guía muestra perfiles de 8K y 64K; el ensayo de velocidad usa un prompt de 565 tokens | Configurar 64K no demuestra procesar un prompt de 64K. La PR está abierta; Todd pidió separar cambios y ampliar la validación. No demuestra 128K ni rendimiento en una 5080 bajo Windows nativo |
| [Kernoriordan, uso con Zoo Code](https://www.reddit.com/r/LocalLLaMA/comments/1wz8qc4/follow_up_qwen_38_27b_at_96ts_decode_with_ninfer/) | RTX 5080, Ubuntu 24.04 en WSL2, runtime v1.5; cliente de programación en Windows | 110.592 posiciones configuradas; historial real de unos 77–79K tokens; mediana de 96,45 tokens/s en 32 turnos | Autoinforme del usuario, sin comparación controlada de calidad. No es Windows nativo ni un prompt efectivo de 110K |
| [Prueba de 懒人烘培](https://lcz.me/topic/1996) | RTX 5080, Windows 11 + WSL2 Ubuntu 24.04, compilación de v1.5 | Perfil 98K con entrada de 96.000 tokens y generación de 2.048; dos repeticiones reportan unos 104 tokens/s de decode | 128K con todas las funciones no entró. Las variantes 128K reducidas solo completaron respuestas cortas; no validan 128K efectivos |
| [Ryan-gsq, guía Windows](https://github.com/Ryan-gsq/ninfer-16g-5070ti-5080-5090-qwen3.8-27b-gsq-rco/blob/main/docs/rtx-5070ti-windows.en.md) | Otro linaje de NInfer, Windows nativo, Swift S/XXS y gestor propio | Ensayos específicos en RTX 5070 Ti y recetas de compilación | Usa otros artefactos; no sirve para atribuir sus cifras al modelo de Todd ni a la integración de Forja |

La [guía del port nativo](https://github.com/YukinoKaorisuna/ninfer-5070ti/blob/rtx5070ti-windows/docs/WINDOWS_5070TI.md) mezcla observaciones históricas y estado reciente, y contiene una contradicción sobre si se ejercitó el servidor. La PR incluye evidencia del servidor y el propio mantenedor reconoce el trabajo, pero solicita corregir esas contradicciones. Por eso Forja no trata el README heredado de 128K como validación Windows.

## Fuente y modelo elegidos

- Motor: `YukinoKaorisuna/ninfer-5070ti`, revisión `aee91065f4d3635a07d681800f87829e18f0e227`, rama de origen `rtx5070ti-windows`.
- Es un port de `toddballinger/ninfer-5080`; no se lo presenta como el binario Linux v1.5.
- Modelo: [artefacto oficial de Todd](https://huggingface.co/ninfer-5080/Qwen3.8-27B-RTX5080), `qwen3_8_27b.ninfer`, 16.461.267.456 bytes.
- SHA-256: `c4a7e9ab593a7f42d58208fa0065d67a82d61921107686cc9f6ed1ec6b050e21`.
- Pesos mixtos Q3/Q4/Q5, receta `groupwise-int-5080`. No se sustituye por el modelo alternativo abliterado que también aparece en el fork.
- Las fuentes y componentes CUDA se fijan en `build/ninfer-windows.lock.json`. El motor se compila en Windows con MSVC 14.44 y CUDA 13.4.1; la ayuda CLI verifica carga de dependencias sin cargar pesos ni hacer inferencia.

## Perfiles iniciales de Forja 50

| Ajuste | RTX 5080 escritorio | RTX 5070 Ti escritorio |
| --- | --- | --- |
| Contexto y capacidad KV | 65.536 | 65.536 |
| Prefill automático | 896 | 512 |
| KV | Q4 | Q4 |
| MTP | 3 borradores | 3 borradores |
| Embeddings en RAM | Sí | Sí |
| CUDA Graph | Sí | Sí |
| Visión inicial | Desactivada | Desactivada |
| Razonamiento / salida | 2.048 / 8.192 | 2.048 / 8.192 |
| Concurrencia | 1 | 1 |
| Prueba física de estos perfiles en Forja | Pendiente | Pendiente |

El prefill de 896 toma una referencia práctica de los ejemplos de 5080/Windows y reduce el bloque Linux de 1792. El de 512 en 5070 Ti es una decisión conservadora de Forja para probar, no una cifra que se anuncie como óptima o validada. La configuración avanzada permite un valor manual o 0 para volver al perfil por placa. Los ajustes manuales sobreviven a la detección de hardware.

128K queda marcado como experimental en ambas placas bajo Windows nativo. El preflight de memoria es orientativo y no garantiza que un perfil entre. No se cambian BIOS, iGPU, controlador, monitores ni aplicaciones ajenas automáticamente.

## Descarga y actualización

El modelo y el motor se descargan después de instalar. El descargador usa dieciséis trabajadores y fragmentos persistentes de 64 MiB: una conexión rápida sigue trabajando aunque otra vaya más lenta. Puede retomar tanto el prefijo `.part` de versiones anteriores como fragmentos incompletos y fuera de orden. El archivo final se habilita únicamente después de comprobar tamaño y SHA-256.

Ante 30 segundos sin respuesta o datos se realizan hasta tres reintentos con espera progresiva. Si el servidor ignora Range se vuelve a una conexión. Las pruebas de red acotadas sirven para comprobar conectividad y paralelismo, no garantizan la velocidad de la descarga completa.

La selección anterior KVMem/Swift se conserva como alternativa, con sus archivos separados. Cambiar de modelo no permite reutilizar los bytes de un `.part` de Swift como si fueran el modelo de Todd. Los proyectos, chats y archivos del usuario se conservan.
