"""Create a local comparison report, CSV and synchronized video from completed benchmark files."""
import argparse
import csv
import hashlib
import html
import json
from pathlib import Path
import statistics
import av
from PIL import Image, ImageDraw

p=argparse.ArgumentParser();p.add_argument('run',type=Path);a=p.parse_args();root=a.run.resolve()
reports=json.loads((root/'report.json').read_text(encoding='utf-8'))
experiment=json.loads((root/'experiment.json').read_text(encoding='utf-8')) if (root/'experiment.json').exists() else {}
labels={'5b':'Wan 2.2 5B FP16','gguf':'Wan 2.2 A14B GGUF Q4_K_S + LightX2V','nvfp4':'LightWan A14B NVFP4 (ComfyUI, SDPA densa)','nvfp4-sol':'LightWan NVFP4 + Sol dispersa (top-k 10%, experimental)'}
gib=lambda x:x/1024**3
rows=[];chosen={}
for r in reports:
    stages=[s for s in r['stages'] if s['event']=='sampled']
    resident=bool(stages) and all(s['loaded_bytes']==s['model_bytes'] and s['lowvram_patches']==0 for s in stages)
    files=list((root/'output').glob(r['prefix']+'*.mp4'))
    row=dict(variant=r['variant'],repeat=r['repeat'],total_seconds=r['seconds'],startup_seconds=r['startup_seconds'],sampling_seconds=sum(s['seconds'] for s in stages),load_seconds=sum(s['seconds'] for s in r['stages'] if s['event']=='loaded'),gpu_total_peak_gib=gib(r['gpu_peak']),gpu_baseline_gib=gib(r['baseline_gpu_used']),gpu_increment_gib=gib(r['gpu_peak']-r['baseline_gpu_used']),torch_allocated_peak_gib=max((gib(s['allocated_peak']) for s in stages),default=0),ram_resident_peak_gib=gib(r['ram_rss_peak']),private_commit_peak_gib=gib(r['ram_private_peak']),system_ram_available_min_gib=gib(r['ram_available_min']),fully_loaded_at_stage_end=resident,nvfp4_matmul_calls=sum(s.get('nvfp4_matmul_calls',0) for s in stages),error=r['error'],file=str(files[0].relative_to(root)) if files else '')
    samples=json.loads((root/r['variant']/f"memory-{r['repeat']}.json").read_text(encoding='utf-8'))
    row.update(gpu_temperature_peak_c=max(x['temperature_c'] for x in samples),gpu_power_peak_w=max(x['power_w'] for x in samples),gpu_power_mean_w=statistics.mean(x['power_w'] for x in samples),gpu_energy_wh=sum((b['time']-a['time'])*(a['power_w']+b['power_w'])/2 for a,b in zip(samples,samples[1:]))/3600)
    if files:
        with av.open(str(files[0])) as video:
            stream=video.streams.video[0]
            row.update(width=stream.width,height=stream.height,fps=float(stream.average_rate),decoded_frames=sum(1 for _ in video.decode(video=0)))
        with files[0].open('rb') as f:row['sha256']=hashlib.file_digest(f,'sha256').hexdigest()
        chosen.setdefault(r['variant'],files[0])
    rows.append(row)
fields=list(dict.fromkeys(k for r in rows for k in r))
with (root/'comparacion.csv').open('w',newline='',encoding='utf-8-sig') as f:
    writer=csv.DictWriter(f,fieldnames=fields);writer.writeheader();writer.writerows(rows)
summaries=[]
for variant in ['5b','gguf','nvfp4','nvfp4-sol']:
    data=[r for r in rows if r['variant']==variant and not r['error']]
    if not data:continue
    warm=[r for r in data if r['repeat']>0]
    summaries.append(dict(variant=variant,label=labels[variant],n=len(data),first_seconds=data[0]['total_seconds'],warm_median_seconds=statistics.median(r['total_seconds'] for r in warm or data),warm_min_seconds=min(r['total_seconds'] for r in warm or data),warm_max_seconds=max(r['total_seconds'] for r in warm or data),sampling_median_seconds=statistics.median(r['sampling_seconds'] for r in warm or data),gpu_peak_gib=max(r['gpu_total_peak_gib'] for r in data),gpu_baseline_gib=data[0]['gpu_baseline_gib'],ram_resident_peak_gib=max(r['ram_resident_peak_gib'] for r in data),private_commit_peak_gib=max(r['private_commit_peak_gib'] for r in data),fully_resident=all(r['fully_loaded_at_stage_end'] for r in data)))
(root/'resumen.json').write_text(json.dumps(summaries,indent=2),encoding='utf-8')
table=''.join(f"<tr><td>{html.escape(s['label'])}</td><td>{s['first_seconds']:.1f} s</td><td>{s['warm_median_seconds']:.1f} s<br><small>{s['warm_min_seconds']:.1f}–{s['warm_max_seconds']:.1f}</small></td><td>{s['sampling_median_seconds']:.1f} s</td><td>{s['gpu_peak_gib']:.2f} GiB</td><td>{s['ram_resident_peak_gib']:.2f} GiB</td><td>{'Sí' if s['fully_resident'] else 'No'}</td></tr>" for s in summaries)
videos=''.join(f'<section><h3>{html.escape(labels[v])}</h3><video controls loop muted preload="metadata" src="{html.escape(path.relative_to(root).as_posix())}"></video></section>' for v,path in chosen.items())
document='''<!doctype html><html lang="es"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Forja · comparación de animación RTX 5080</title><style>body{background:#101820;color:#e2ecf1;font:16px Segoe UI,sans-serif;margin:0 auto;padding:28px;max-width:1400px}h1{font-size:28px}p,li{line-height:1.55}table{border-collapse:collapse;width:100%;margin:24px 0}td,th{padding:13px;border:1px solid #38505e;text-align:left}th,section{background:#1c2933}small{color:#a9bec8}.videos{display:grid;grid-template-columns:repeat(3,1fr);gap:16px}section{padding:12px;border-radius:8px}video{width:100%}button{background:#087f70;color:white;border:0;padding:13px;border-radius:6px;cursor:pointer}a{color:#5bd3bd}@media(max-width:850px){.videos{grid-template-columns:1fr}.scroll{overflow:auto}}.notice{background:#23434b;padding:16px;border-radius:8px}</style><h1>Comparación real de animación · RTX 5080 de 16 GB</h1><p>Misma referencia, texto y semilla. 576 × 576, 81 fotogramas, 24 FPS: clips de 3,375 segundos. Tres pedidos por variante; el primero separado de los otros dos. Windows, PyTorch 2.14.0 + CUDA 13.0, ComfyUI 0.42.1. Controlador 621.01.</p><p class="notice">Esta comparación usa atención <b>PyTorch SDPA densa</b>. NVFP4 ejecuta operaciones aceleradas reales, pero no es el runtime LightX2V con atención dispersa de sus benchmarks de 5090. No extrapolar los tiempos a la 5070 Ti.</p><div class="scroll"><table><thead><tr><th>Variante</th><th>Primer pedido</th><th>Mediana pedidos 2–3</th><th>Solo muestreo</th><th>Pico VRAM total</th><th>Pico RAM residente del motor</th><th>Experto completo al terminar cada etapa</th></tr></thead><tbody>'''+table+'''</tbody></table></div><p>El tiempo total incluye interpretar el texto, cargar los expertos, generar, decodificar y exportar; el inicio del motor se registra aparte. Los expertos alto/bajo se intercambian entre etapas, no se mantienen juntos. El codificador y el VAE tienen etapas separadas. Caché de nodos desactivada y memoria fijada desactivada en las tres variantes.</p><p>La VRAM es el total de la placa, incluyendo escritorio y otras aplicaciones. Muestreo cada 200 ms: los picos instantáneos pueden ser mayores. El CSV incluye el pico de asignaciones Torch durante difusión, memoria privada comprometida y RAM libre. El compromiso privado puede superar la RAM física; no confundirlo con memoria residente.</p><h2>Compará los videos</h2><p><button onclick="document.querySelectorAll('video').forEach(v=>{v.currentTime=0;v.play()})">Reproducir los tres desde el inicio</button> <button onclick="document.querySelectorAll('video').forEach(v=>v.pause())">Pausar todos</button></p><div class="videos">'''+videos+'''</div><h2>Qué mirar</h2><ul><li>¿Las manos se mueven hacia afuera y aparece el anillo mágico solicitado?</li><li>¿Conserva cara, proporciones, ropa y fondo?</li><li>¿Hay manos extra, deformaciones, parpadeos o cambios de identidad?</li><li>¿El pelo y la capa se mueven de forma coherente, sin que la cámara sustituya la acción?</li></ul><p>Es una única escena. Repetir la misma semilla mide tiempos, no demuestra calidad general. Compará el clip completo, no solo fotogramas favorables. Las comprobaciones de carga de pesos no certifican que Windows nunca pagine memoria gráfica por su cuenta.</p><p><a href="comparacion.csv">Descargar CSV de todas las ejecuciones</a> · <a href="resumen.json">Resumen numérico</a> · <a href="report.json">Registros originales</a> · <a href="input/reference.png">Imagen de referencia</a></p></html>'''
document=document.replace('576 × 576', f"{experiment.get('width',576)} × {experiment.get('height',576)}")
if 'nvfp4-sol' in chosen:
    document=document.replace('grid-template-columns:repeat(3,1fr)','grid-template-columns:repeat(2,1fr)').replace('Reproducir los tres','Reproducir todos')
    document=document.replace('Esta comparación usa atención <b>PyTorch SDPA densa</b>.', 'Las tres variantes de referencia usan <b>PyTorch SDPA densa</b>. La cuarta usa <b>Sol dispersa con top-k 10%</b>, implementada en los operadores CUDA ya instalados. Es una prueba adicional; no es la implementación Sage2 del runtime LightX2V.')
    document=document.replace('Las comprobaciones de carga', 'Sol se midió en un lote posterior: la ocupación inicial de Windows puede ser distinta. Comparar también el incremento sobre la VRAM de base en el CSV. Las comprobaciones de carga')
(root/'comparacion.html').write_text(document,encoding='utf-8')
print(json.dumps(summaries,indent=2),flush=True)
if len(chosen)>=3:
    order=[v for v in ['5b','gguf','nvfp4','nvfp4-sol'] if v in chosen];containers=[av.open(str(chosen[v])) for v in order]
    width,height=(768,820) if len(order)==4 else (1152,410)
    try:
        output=av.open(str(root/'comparacion-lado-a-lado.mp4'),'w');stream=output.add_stream('libx264',rate=24);stream.width=width;stream.height=height;stream.pix_fmt='yuv420p';stream.options={'crf':'18'}
        for frames in zip(*(container.decode(video=0) for container in containers)):
            canvas=Image.new('RGB',(width,height),'#101820');draw=ImageDraw.Draw(canvas)
            for i,(variant,frame) in enumerate(zip(order,frames)):
                x,y=((i%2)*384,(i//2)*410) if len(order)==4 else (i*384,0)
                image=frame.to_image();image.thumbnail((384,384));canvas.paste(image,(x,y+26));draw.text((x+8,y+7),{'5b':'Wan 5B','gguf':'Wan 14B GGUF Q4','nvfp4':'LightWan NVFP4','nvfp4-sol':'NVFP4 + Sol top-k 10%'}[variant],fill='white')
            for packet in stream.encode(av.VideoFrame.from_image(canvas)):output.mux(packet)
        for packet in stream.encode():output.mux(packet)
        output.close()
    finally:
        for container in containers:container.close()
