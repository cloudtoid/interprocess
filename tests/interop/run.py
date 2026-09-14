"""Run every ordered language pair against a fresh, repeatedly wrapping queue.

Build drivers first (see README). INTEROP_LANGUAGES can select a subset during
local development; CI runs all six. Process deadlines make stalls fail loudly.
"""
import itertools
import os
from pathlib import Path
import queue
import subprocess
import sys
import tempfile
import threading

ROOT = Path(__file__).resolve().parents[2]
EXE = '.exe' if os.name == 'nt' else ''
COMMANDS = {
    'rust': [str(ROOT / f'target/release/examples/interop{EXE}')],
    'c': [str(ROOT / f'target/interop/c-driver{EXE}')],
    'python': [sys.executable, str(ROOT / 'tests/interop/python_driver.py')],
    'node': [os.environ.get('NODE', 'node'), str(ROOT / 'tests/interop/node_driver.js')],
    'go': [str(ROOT / f'target/interop/go-driver{EXE}')],
    'dotnet': [os.environ.get('DOTNET_HOST_PATH', 'dotnet'), str(ROOT / 'tests/interop/dotnet/bin/Release/net10.0/Interop.dll')],
}
LANGUAGES = os.environ.get('INTEROP_LANGUAGES', ','.join(COMMANDS)).split(',')
COUNT = os.environ.get('INTEROP_COUNT', '2000')

def ready(process):
    lines = queue.Queue()
    threading.Thread(target=lambda: lines.put(process.stdout.readline()), daemon=True).start()
    try: line = lines.get(timeout=15)
    except queue.Empty: raise RuntimeError('subscriber startup timed out')
    if line.strip() != 'READY': raise RuntimeError(f'subscriber did not start: {line!r}')

with tempfile.TemporaryDirectory(prefix='cip-interop-') as path:
    for number, (writer, reader) in enumerate(itertools.product(LANGUAGES, repeat=2)):
        name = f'i{os.getpid()}x{number}'
        args = [name, path, COUNT]
        with tempfile.TemporaryFile(mode='w+') as errors:
            subscriber = subprocess.Popen(COMMANDS[reader] + ['subscribe', *args], stdout=subprocess.PIPE, stderr=errors, text=True)
            try:
                ready(subscriber)
                subprocess.run(COMMANDS[writer] + ['publish', *args], check=True, timeout=45)
                subscriber.communicate(timeout=45)
                if subscriber.returncode:
                    errors.seek(0)
                    raise RuntimeError(errors.read())
                print(f'PASS {writer:6} -> {reader:6}: {COUNT} messages', flush=True)
            finally:
                if subscriber.poll() is None:
                    subscriber.kill()
                    subscriber.wait()
print(f'PASS: {len(LANGUAGES)**2} language pairs', flush=True)
