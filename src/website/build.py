"""Build the static site for Sites or GitHub Pages, without runtime dependencies."""
from pathlib import Path
import shutil
root = Path(__file__).resolve().parent
output = root / 'dist'
output.mkdir(exist_ok=True)
for name in ('index.html', 'style.css', 'site.js'):
    shutil.copyfile(root / name, output / name)
(output / '.nojekyll').touch()
print(f'Built {output}')
