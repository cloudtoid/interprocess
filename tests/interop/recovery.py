"""Recover a claimed record using the other implementation's participant lease.

The victim owns a real lease but is paused in the hold driver. Inject the crash
state while no participant is reading or writing, then kill the victim before
starting the recovering reader. No product fault-injection API is needed.
"""
import mmap
import os
from pathlib import Path
import struct
import subprocess
import tempfile
import time

from cloudtoid_interprocess import Publisher
from run import COMMANDS, ready

CAPACITY = 4096
BUFFER = 262400

with tempfile.TemporaryDirectory(prefix='cip-recovery-') as path:
    for victim_language, reader_language in [('rust', 'dotnet'), ('dotnet', 'rust')]:
        name = f'rec{os.getpid()}{victim_language[0]}'
        children = []
        with Publisher(name, CAPACITY, path) as publisher, tempfile.TemporaryFile(mode='w+') as errors:
            try:
                victim = subprocess.Popen(COMMANDS[victim_language] + ['hold-subscriber', name, path, '0'],
                                          stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=errors, text=True)
                children.append(victim)
                ready(victim)
                assert publisher.try_send(b'abandoned')
                if os.name == 'nt':
                    view = mmap.mmap(-1, BUFFER + CAPACITY, tagname=f'CT3_IP_{name}')
                else:
                    backing = Path(path) / '.cloudtoid/interprocess/v3/mmf' / f'{name}.qu'
                    with backing.open('r+b') as file:
                        view = mmap.mmap(file.fileno(), 0)
                with view:
                    owner = struct.unpack_from('<i', view, 28)[0]
                    assert owner == 2  # Publisher first, then the victim's subscriber.
                    tail = struct.unpack_from('<q', view, 8)[0]
                    struct.pack_into('<q', view, 16, owner)
                    struct.pack_into('<i', view, BUFFER, 1)  # Claimed, not consumed.
                    victim.kill()
                    victim.wait(timeout=10)
                    assert victim.returncode != 0
                    reader = subprocess.Popen(COMMANDS[reader_language] + ['subscribe', name, path, '1'],
                                              stdout=subprocess.PIPE, stderr=errors, text=True)
                    children.append(reader)
                    ready(reader)
                    deadline = time.monotonic() + 25
                    while struct.unpack_from('<q', view, 0)[0] != tail:
                        assert reader.poll() is None, 'recovering reader exited before repair'
                        assert time.monotonic() < deadline, 'claimed record was not recovered'
                        time.sleep(.01)
                    # The ordinary pair driver validates message zero byte-for-byte.
                    while not publisher.try_send(bytes(8)):
                        assert time.monotonic() < deadline, 'recovery gate did not reopen'
                        time.sleep(.001)
                    reader.wait(timeout=10)
                    errors.seek(0)
                    assert reader.returncode == 0, errors.read()
                print(f'PASS: {reader_language} repairs killed {victim_language} reader and receives next message', flush=True)
            finally:
                for child in children:
                    if child.poll() is None: child.kill()
                    child.wait(timeout=10)
