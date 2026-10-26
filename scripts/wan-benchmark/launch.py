"""Load isolated GGUF dependencies, then launch the existing Comfy source unchanged."""
from pathlib import Path
import runpy
import sys

root, engine = map(Path, sys.argv[1:3])
sys.path.insert(0, str(root / "deps"))
sys.path.insert(0, str(engine))
sys.argv = [str(engine / "main.py"), *sys.argv[3:]]
runpy.run_path(str(engine / "main.py"), run_name="__main__")
