"""Check cross-language capacity agreement and both final-cleanup implementations."""
import os
import subprocess
import tempfile
from cloudtoid_interprocess import Publisher, Subscriber, CapacityMismatchError
from run import COMMANDS, LANGUAGES, ready

with tempfile.TemporaryDirectory(prefix='cip-lifetime-') as path:
    for creator in ('rust', 'dotnet'):
        for last in ('native', 'creator'):
            name = f'life{os.getpid()}{creator[0]}{last[0]}'
            if os.name != 'nt': name += '\\x'  # Existing .NET Unix queue names remain interoperable.
            with tempfile.TemporaryFile(mode='w+') as errors:
                child = subprocess.Popen(COMMANDS[creator] + ['hold-subscriber', name, path, '0'],
                                         stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=errors, text=True)
                anchor = None
                try:
                    ready(child)
                    for language in LANGUAGES:
                        env = dict(os.environ, INTEROP_CAPACITY='8192')
                        result = subprocess.run(COMMANDS[language] + ['publish', name, path, '0'], env=env,
                                                capture_output=True, text=True, timeout=30)
                        assert result.returncode != 0, f'{creator} -> {language}: accepted wrong capacity'
                        assert 'capacity' in result.stderr.lower(), (language, result.stderr)
                    anchor = Publisher(name, 4096, path)
                    if last == 'creator': anchor.close()
                    child.stdin.write('close\n'); child.stdin.flush()
                    child.wait(timeout=15)
                    errors.seek(0)
                    assert child.returncode == 0, errors.read()
                    if last == 'native':
                        assert anchor.try_send(b'survived')
                        with Subscriber(name, 4096, path) as receiver:
                            assert receiver.receive(timeout=1) == b'survived'
                        anchor.close()
                    with Subscriber(name, 8192, path) as fresh:
                        assert fresh.try_receive() is None
                    print(f'PASS: {creator} creator, {last} final close; all capacity mismatches rejected', flush=True)
                finally:
                    if anchor is not None: anchor.close()
                    if child.poll() is None: child.kill(); child.wait()
