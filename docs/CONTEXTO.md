# Revisión de contexto — 20 de octubre de 2026

La instalación de referencia tiene contexto lógico 32.768, historial GPU 24.576, reserva de generación 8.192 y caché KV en RAM 2.048 MiB. Agente y motor coinciden en 32.768. El límite de salida es 8.192; razonamiento por defecto 2.048. Los registros registran una entrada de 31.798 tokens y posterior resumen automático. Por tanto el límite de 32K sí se alcanza durante el trabajo real.

Por pedido del usuario, la nueva aplicación usa 65.536 tokens por defecto. En 64K aumenta la caché KV en RAM a 4.096 MiB y mantiene historial GPU 24.576 y reserva 8.192. No aumenta el límite de salida. Cambiar de perfil requiere detener la IA desde la interfaz; el supervisor también recarga el modelo si cambian el perfil o los ajustes avanzados. Se conserva 32K como alternativa.

KVMem separa ventana lógica, selección del historial en GPU y almacenamiento de KV en RAM. 64K lógicos no equivalen a atención simultánea sobre 64K tokens completos. Una ventana mayor puede reducir la frecuencia de los resúmenes, pero no garantiza recuerdo perfecto ni mejor calidad. La cuota de caché RAM tampoco limita toda la RAM del proceso.

Prueba física de 64K completada en esta RTX 5080: carga del motor en 3,9 s después de verificar el modelo; entradas de 43.345 y 52.949 tokens reales, con recuperación correcta de tres valores aleatorios situados al inicio, medio y final en ambos casos. Tiempos totales: 50,3 s y 62,5 s. Se limitó el razonamiento de la prueba a 128 tokens y la salida a 1.024. No es una medición de rendimiento de programación ni una prueba exhaustiva de calidad.

Durante la primera entrada se observaron 14.394 MiB de VRAM usados en todo el equipo y 1.584 MiB libres. Otra muestra durante procesamiento mostró 14.476 MiB usados y 1.502 MiB libres. La memoria física libre observada fue de 18,23 GiB; el proceso mostró 2,72 GiB de working set y 15,65 GiB privados en una muestra. Son muestras, no máximos certificados. La detención liberó la memoria del motor; quedaron unos 14.609 MiB libres en GPU.

El usuario autorizó también 64K en su instalación anterior: se actualizaron motor y agente a 65.536 y caché RAM a 4.096 MiB, conservando un respaldo fechado de los dos JSON anteriores. No se habilitan 128K o 256K basándose solamente en resultados de otros equipos.

## Validación pendiente

Pendiente: sesiones prolongadas de programación con 64K, entradas cerca de 65.536 y errores por exceso de contexto sin perder el chat. La integración de herramientas/Git y reinicio pasó con el perfil de referencia. Repetir estas comprobaciones en una 5070 Ti física antes de anunciar soporte validado en esa placa.

Referencia técnica: [documentación oficial de KVMem](https://github.com/kvmem/kvmem/tree/v0.18.0). Las recetas de otras GPU o formatos KV no sustituyen estas pruebas.
