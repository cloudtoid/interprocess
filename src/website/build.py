"""Build the static site without runtime dependencies."""
from pathlib import Path
import shutil
root = Path(__file__).resolve().parent
output = root / 'dist'
output.mkdir(exist_ok=True)
for name in ('index.html', 'style.css', 'site.js', 'theme.js'):
    shutil.copyfile(root / name, output / name)
for name in ('assets', 'benchmarks', 'vendor'):
    shutil.copytree(root / name, output / name, dirs_exist_ok=True)
print(f'Built {output}')
