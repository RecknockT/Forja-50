# Referencias y calidad: comparación y propuesta

Revisión del 21 de octubre de 2026. Las capacidades publicadas por cada fabricante no son pruebas físicas en las placas de Forja.

## Diagnóstico de Forja 0.3.6

El código reconoce nombres mediante reglas y ordena páginas de Wikipedia por su título. No analiza visualmente los candidatos. Descarga una imagen de la ficha y pasa un único ReferenceLatent a FLUX.2 Klein 4B. No dispone de un asistente visual que compruebe personaje, vestimenta, encuadre o resultado. El Qwen3 4B instalado es el codificador de texto; no está expuesto como un asistente multimodal con herramientas de búsqueda.

Se encontró además una deformación concreta: ImageScale ajustaba la referencia al ancho y alto finales sin conservar su proporción. Una cara o figura vertical podía ensancharse al generar una imagen cuadrada.

## Qué hacen otras herramientas

| Herramienta | Comportamiento documentado | Qué conviene adoptar |
|---|---|---|
| Midjourney | Separa referencia de personaje/objeto y estilo. Omni Reference corresponde a V7; la documentación actual dirige V8.x al Edit Model. | Separar «quién», «estilo» y «escena»; no tratar cualquier imagen como una instrucción única. |
| InvokeAI | Tarjetas de referencias con activación, orden y recorte. Las posibilidades dependen del modelo; FLUX.2 lee las imágenes directamente. Ofrece ampliación de pedidos con LLM y descripción con VLM. | Vista previa, selección explícita del área útil y asistente opcional que convierta una frase en instrucciones visuales. |
| Krita AI Diffusion | Capas de control para referencia, estilo/composición y otras guías. | Incorporar después pose/composición como controles independientes; no reemplazar identidad por una imagen de pose. |

Fuentes: [Midjourney Omni Reference](https://docs.midjourney.com/hc/en-us/articles/36285124473997-Omni-Reference), [InvokeAI Generate Panel](https://v7.invoke.ai/users-guide/image-generation/generate-panel/), [Krita Control Layers](https://github.com/Acly/krita-ai-diffusion/blob/main/docs/src/content/docs/control-layers.mdx).

No hay evidencia en estas fuentes de que las herramientas busquen y verifiquen automáticamente en internet cualquier personaje escrito. Su manejo de imágenes aportadas por el usuario es una referencia de diseño; una búsqueda automática fiable es trabajo adicional de Forja.

## Modelos a comparar

| Modelo | Motivo para evaluarlo | Límite o coste relevante |
|---|---|---|
| FLUX.2 Klein 4B actual | Base rápida, ya descargada y probada puntualmente en la 5080. | Una referencia deficiente sigue limitando el resultado; no arreglarlo simplemente aumentando los cuatro pasos del modelo destilado. |
| FLUX 3 Image Klein | Publicado el 15/10/2026; 5,6B en el generador, hasta diez referencias y control por regiones. | Requiere un ampliador de pedidos; 16 pasos en el destilado. Nueva licencia y componentes distintos. |
| Qwen-Image-2.1 / Turbo | Publicado el 20/09; Turbo el 09/10. Generación y edición, hasta diez referencias y transparencia. | 7B se refiere solo al generador: utiliza además Qwen3-VL 8B. Licencia de investigación; no elegirlo como predeterminado distribuible sin revisar las condiciones. |
| Qwen-Image-Edit-2511 | Alternativa para comparar conservación de identidad; su ficha declara mejoras sobre 2509. | Generador de 20B, más exigente para 16 GB; no es el mismo modelo que Qwen-Image-2.1. |

Fuentes: [anuncio de FLUX 3](https://bfl.ai/blog/flux-3-image-klein), [código y memoria de FLUX 3](https://github.com/black-forest-labs/flux-3-image-klein), [Qwen-Image-2.1](https://github.com/QwenLM/Qwen-Image-2.1), [Qwen-Image-Edit-2511](https://huggingface.co/Qwen/Qwen-Image-Edit-2511), [licencia Qwen 2.1](https://huggingface.co/Qwen/Qwen-Image-2.1/blob/main/LICENSE).

FLUX 3 es el primer candidato de evaluación, no un ganador comprobado. Su código oficial advierte que un pedido corto sin ampliación da resultados más débiles. El ampliador oficial es un VLM de 9B; también admite otro VLM con sus instrucciones de sistema. El codificador usa Qwen3-VL 4B, diferente del Qwen3 textual instalado. No asumir que podemos reutilizar los pesos actuales.

Los archivos ComfyUI incluyen INT8 y otras precisiones. Para 3090, evaluar INT8 con transferencia a RAM; no elegir la ruta FP8/NVFP4 como si fuera Blackwell. Para 5070 Ti/5080, empezar también por INT8 con una referencia y 1 MP, y medir antes de intentar otras precisiones o varias referencias. Los tamaños de los pesos no equivalen al consumo total. El código oficial sin descarga a RAM publica picos superiores a 24 GB, por lo que no debe copiarse directamente para nuestras placas.

Fuentes: [código oficial, Smaller GPUs y Memory](https://github.com/black-forest-labs/flux-3-image-klein), [paquete ComfyUI oficial](https://huggingface.co/black-forest-labs/flux-3-image-klein-comfy). En el runtime local 0.42.1 se encontraron clases y loaders flux3; esto confirma presencia de código, no inferencia ni rendimiento.

## Orden recomendado

1. **Corregir la entrada.** Mostrar la imagen real antes de generar, permitir cambiarla por una local, indicar resolución baja y conservar proporciones. No convertir un estilo general como «anime de los 90» en la elección obligatoria de un personaje.
2. **Ficha de personaje.** Guardar referencias aprobadas y rasgos esenciales —cabello, ojos, ropa y accesorios— por separado del pedido de pose, estilo y escenario. Permitir cambiar un rasgo expresamente. Mantener la referencia original en las ediciones para reducir la deriva acumulada.
3. **Asistente visual local.** Evaluar un VLM como Qwen3-VL 4B: describir y comparar candidatos, advertir retratos pequeños, logos o varios personajes y preparar el pedido. Sus descripciones son propuestas, no pruebas de identidad. Cargarlo antes de la difusión y descargarlo de la GPU para no sumar dos modelos residentes en 16 GB. [Ficha oficial Qwen3-VL](https://huggingface.co/Qwen/Qwen3-VL-4B-Instruct).
4. **Comparar modelos con las mismas referencias.** Probar Klein actual y FLUX 3 completo, con el ampliador correspondiente. No migrar el valor predeterminado hasta comprobar resultados y memoria. Qwen 2.1 queda como comparación técnica por sus condiciones de licencia.
5. **Búsqueda más amplia y varias referencias.** Añadir un proveedor de búsqueda con API estable y procedencia visible. Resolver proveedor, disponibilidad, licencias y autenticación antes de prometer búsquedas generales sin cuenta. Wikipedia sola no cubre adecuadamente anime; tampoco conviene depender de extraer HTML cambiante de un buscador. Elegir pocas imágenes coherentes y asignarles función; diez imágenes no garantizan mejor identidad.
6. **Edición localizada y animación.** Añadir selección de región para «solo el brazo rojo». Primero aprobar una imagen fiel; usarla como primer fotograma de Wan. Un generador de video no corrige por sí mismo una identidad incorrecta.

## Prueba de aceptación propuesta

Usar Frieren en una pose distinta, otro personaje, un personaje inventado con referencia local, un estilo general sin personaje y una edición del brazo. Comparar también un retrato pequeño y una imagen con varios sujetos. Repetir con tres semillas dentro de cada modelo y registrar pedido original/ampliado, referencias, versión, precisión, pasos, tamaño, tiempo y memoria. Puntuar por separado identidad, vestimenta, seguimiento de instrucciones, estilo y defectos. Comparar la misma semilla entre arquitecturas no garantiza el mismo ruido o dificultad.

En 5080 se puede hacer la primera prueba física. 5070 Ti y 3090 requieren su propia validación: una simulación de perfiles no prueba la inferencia. Mantener pruebas aisladas y los modelos anteriores hasta elegir. No descargar nuevos pesos como parte de esta investigación.

## Trabajo preparado en este cambio

Se prepararon revisión visual antes de generación, reemplazo local, advertencia de baja resolución, referencias locales para escenas nuevas, vista previa JPEG y ajuste de proporciones en imagen/edición mediante ForjaFitReference. Se solicitan miniaturas de hasta 1024 píxeles, lo que no aumenta el detalle de un original pequeño. Las pruebas de protocolo, grafo y ventanas pasan sin cargar modelos. No se han descargado ni probado FLUX 3 o Qwen 2.1 y no se modificó la aplicación abierta del usuario.
