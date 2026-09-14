"""All languages publish and compete concurrently, across process crashes.

Each record carries a unique ID and deterministic payload. Collectors validate
the bytes; this coordinator verifies no missing or duplicate deliveries. Killed
endpoints hold real registrations but do not own an in-flight message. Rust's
fault-injection tests separately cover abandoned reservations and reader locks.
"""
from contextlib import ExitStack
import os
import queue
import subprocess
import tempfile
import threading
import time

from cloudtoid_interprocess import Publisher
from run import COMMANDS, LANGUAGES, ready

COUNT = int(os.environ.get('INTEROP_MIXED_COUNT', '2000'))
assert COUNT > 0
events = queue.Queue()
children = []

with tempfile.TemporaryDirectory(prefix='cip-mixed-') as path, ExitStack() as stack:
    name = f'm{os.getpid()}'

    def start(language, mode, offset=None):
        errors = stack.enter_context(tempfile.TemporaryFile(mode='w+'))
        args = COMMANDS[language] + [mode, name, path, str(COUNT)]
        if offset is not None:
            args.append(str(offset))
        child = subprocess.Popen(args, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                 stderr=errors, text=True)
        children.append((child, errors))
        ready(child)
        return child, errors

    def wait(child, errors):
        child.wait(timeout=45)
        if child.returncode:
            errors.seek(0)
            raise AssertionError(errors.read() or f'child exited {child.returncode}')

    def collect(language, child):
        for line in child.stdout:
            events.put((language, line.strip()))
        events.put((language, None))

    try:
        with Publisher(name, 4096, path) as anchor:
            collectors = []
            threads = []
            for language in LANGUAGES:
                child, errors = start(language, 'collect')
                collectors.append((child, errors))
                thread = threading.Thread(target=collect, args=(language, child), daemon=True)
                thread.start()
                threads.append(thread)
            victims = [start('rust', role)[0] for role in ('hold-publisher', 'hold-subscriber')]

            seen = set()
            counts = {language: 0 for language in LANGUAGES}
            total = 2 * COUNT * len(LANGUAGES)

            def received():
                language, line = events.get(timeout=30)
                assert line is not None, f'{language} collector exited early'
                value = int(line)
                assert 0 <= value < total, f'out-of-range ID {value}'
                assert value not in seen, f'duplicate ID {value}'
                seen.add(value)
                counts[language] += 1

            for phase in range(2):
                publishers = [start(language, 'publish', (phase * len(LANGUAGES) + i) * COUNT)
                              for i, language in enumerate(LANGUAGES)]
                # All publishers and collectors are registered before traffic starts.
                for child, _ in publishers:
                    child.stdin.write('GO\n')
                    child.stdin.flush()
                if phase == 0:
                    received()
                    for child in victims:
                        assert child.poll() is None, 'crash target exited before it was killed'
                        child.kill()
                        child.wait(timeout=10)
                        assert child.returncode != 0
                for child, errors in publishers:
                    wait(child, errors)

            while len(seen) < total:
                received()
            # One empty sentinel per live collector; each exits on its first one.
            deadline = time.monotonic() + 10
            for _ in collectors:
                while not anchor.try_send(b''):
                    assert time.monotonic() < deadline, 'could not stop collectors'
                    time.sleep(.001)
            for child, errors in collectors:
                wait(child, errors)
            for thread in threads:
                thread.join(timeout=5)
                assert not thread.is_alive()
            while not events.empty():
                language, line = events.get_nowait()
                assert line is None, f'extra delivery from {language}: {line}'
            print(f'PASS: mixed {len(LANGUAGES)} publishers / {len(LANGUAGES)} subscribers, '
                  f'{total} unique messages, killed publisher + subscriber; deliveries {counts}', flush=True)
    finally:
        for child, _ in children:
            if child.poll() is None:
                child.kill()
            child.wait(timeout=10)
            child.stdin.close()
            child.stdout.close()
