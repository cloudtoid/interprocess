"""Build and test all language packages, then prepare the pair-matrix drivers."""
import os
from pathlib import Path
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
os.chdir(ROOT)
ENV = os.environ.copy()
SDK = ROOT / 'target/sdk'
ENV['PKG_CONFIG_PATH'] = str(SDK / 'lib/pkgconfig')
ENV['LD_LIBRARY_PATH'] = str(SDK / 'lib')
ENV['DYLD_LIBRARY_PATH'] = str(SDK / 'lib')
ENV['PATH'] = str(SDK / 'lib') + os.pathsep + ENV['PATH']
ENV['PYO3_PYTHON'] = sys.executable
ENV['DOTNET_HOST_PATH'] = ENV.get('DOTNET_HOST_PATH', shutil.which('dotnet') or 'dotnet')
EXE = '.exe' if os.name == 'nt' else ''

def run(*args, cwd=ROOT):
    subprocess.run([str(a) for a in args], cwd=cwd, env=ENV, check=True)

run('cargo', 'test', '--release', '--locked', '-p', 'cloudtoid-interprocess')
run('cargo', 'build', '--release', '--locked', '-p', 'cloudtoid-interprocess', '--example', 'interop')
run('cmake', '-S', 'src/c', '-B', 'target/c-sdk', f'-DCMAKE_INSTALL_PREFIX={SDK}', '-DCMAKE_INSTALL_LIBDIR=lib')
run('cmake', '--build', 'target/c-sdk', '--config', 'Release')
run('cmake', '--install', 'target/c-sdk', '--config', 'Release')
run(sys.executable, '-m', 'pip', 'install', 'maturin>=1.9,<2')
run(sys.executable, '-m', 'maturin', 'build', '--release', '--locked', '--manifest-path', 'src/python/Cargo.toml', '--out', 'target/wheels')
wheel = next((ROOT / 'target/wheels').glob('*.whl'))
run(sys.executable, '-m', 'pip', 'install', '--force-reinstall', wheel)
run(sys.executable, 'src/python/test_api.py')
run('node', 'src/node/build.js')
run('node', '--test', 'src/node/test.js')
run('go', 'test', '-race', './...', cwd=ROOT / 'src/go')
run(ENV['DOTNET_HOST_PATH'], 'build', 'tests/interop/dotnet/Interop.csproj', '-c', 'Release')
(ROOT / 'target/interop').mkdir(exist_ok=True)
run('gcc' if os.name == 'nt' else 'cc', 'tests/interop/c_driver.c', f'-I{SDK / "include"}', f'-L{SDK / "lib"}', '-lcloudtoid_interprocess', '-o', f'target/interop/c-driver{EXE}')
run('go', 'build', '-o', ROOT / f'target/interop/go-driver{EXE}', './cmd/interop', cwd=ROOT / 'src/go')
run(sys.executable, 'tests/interop/run.py')
run(sys.executable, 'tests/interop/mixed.py')
