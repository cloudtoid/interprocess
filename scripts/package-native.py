"""Assemble tested CI binaries into npm packages, Python wheels, and C SDKs."""
import json
from pathlib import Path
import shutil
import sys
import tarfile

ROOT = Path(__file__).resolve().parents[1]
artifacts = Path(sys.argv[1]).resolve()
output = Path(sys.argv[2]).resolve()
output.mkdir(parents=True, exist_ok=True)
base = json.loads((ROOT / 'src/node/package.json').read_text())
version = base['version']
platforms = {
    'packages-Linux-X64': ('linux', 'x64'),
    'packages-Linux-ARM64': ('linux', 'arm64'),
    'packages-Windows-X64': ('win32', 'x64'),
    'packages-macOS-ARM64': ('darwin', 'arm64'),
    'packages-macOS-X64': ('darwin', 'x64'),
}
for artifact, (platform, arch) in platforms.items():
    source = artifacts / artifact
    binary = source / f'src/node/interprocess.{platform}-{arch}.node'
    if not binary.is_file(): raise SystemExit(f'Missing tested/cross-built binary: {binary}')
    package = output / 'npm' / f'{platform}-{arch}'
    package.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(binary, package / binary.name)
    shutil.copyfile(ROOT / 'LICENSE', package / 'LICENSE')
    manifest = {
        'name': f'@cloudtoid/interprocess-{platform}-{arch}', 'version': version,
        'description': f'Cloudtoid Interprocess native binary for {platform} {arch}',
        'main': binary.name, 'files': [binary.name, 'LICENSE'],
        'os': [platform], 'cpu': [arch], 'license': 'MIT',
        'repository': base['repository'], 'engines': base['engines'],
    }
    (package / 'package.json').write_text(json.dumps(manifest, indent=2)+'\n')
    wheels = output / 'python'
    wheels.mkdir(exist_ok=True)
    for wheel in (source / 'target/wheels').glob('*.whl'): shutil.copyfile(wheel, wheels / wheel.name)
    sdk = source / 'target/sdk'
    with tarfile.open(output / f'cloudtoid-interprocess-{version}-{platform}-{arch}.tar.gz', 'w:gz') as archive:
        archive.add(sdk, arcname=f'cloudtoid-interprocess-{version}')
main = output / 'npm/main'
main.mkdir(parents=True, exist_ok=True)
for name in ('package.json', 'index.js', 'index.d.ts', 'README.md', 'LICENSE'):
    shutil.copyfile(ROOT / 'src/node' / name, main / name)
print(f'Assembled version {version} in {output}')
