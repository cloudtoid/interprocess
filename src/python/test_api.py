import os
import unittest
import pathlib
import tempfile
import threading
import time
import signal
import sys
from cloudtoid_interprocess import Publisher, Subscriber, CapacityMismatchError, InterprocessError

class QueueTests(unittest.TestCase):
    def test_api(self):
        name = f"py{os.getpid()}"
        with Subscriber(name, 64) as s, Publisher(name, 64) as p:
            self.assertIsNone(s.try_receive())
            self.assertTrue(p.try_send(b""))
            self.assertEqual(s.try_receive(), b"")
            self.assertEqual(p.try_send_batch([b"a", b"b"]), 2)
            self.assertEqual(s.receive(timeout=0), b"a")
            self.assertEqual(s.receive(timeout=1), b"b")
            self.assertIsNone(s.receive(timeout=0.005))
            with self.assertRaises(ValueError): s.receive(timeout=-1)
        with self.assertRaises(ValueError): p.try_send(b"closed")
        with self.assertRaises(ValueError): s.try_receive()

    def test_buffers_paths_and_errors(self):
        with tempfile.TemporaryDirectory() as path:
            with Subscriber("buffers", 64, path=pathlib.Path(path)) as s, Publisher("buffers", 64, path=pathlib.Path(path)) as p:
                for data in [bytearray(b"abc"), memoryview(b"abc"), memoryview(b"aXbXc")[::2]]:
                    self.assertTrue(p.try_send(data))
                    self.assertEqual(s.try_receive(), b"abc")
                self.assertEqual(p.try_send_batch([bytearray(b"a"), memoryview(b"b")]), 2)
                self.assertEqual(s.receive(), b"a")
                self.assertEqual(s.receive(), b"b")
                with self.assertRaises(InterprocessError) as caught: Publisher("buffers", 128, path=path)
                self.assertIsInstance(caught.exception, CapacityMismatchError)
                self.assertIsInstance(caught.exception, ValueError)
                with self.assertRaises(ValueError): Publisher("bad/name", 64, path=path)

    def test_close_interrupts_wait_and_releases_gil(self):
        s = Subscriber(f"close{os.getpid()}", 64)
        errors = []
        started = threading.Event()
        def wait():
            started.set()
            try: s.receive()
            except Exception as error: errors.append(error)
        thread = threading.Thread(target=wait)
        thread.start()
        self.assertTrue(started.wait(1))
        time.sleep(0.02)
        s.close()
        thread.join(2)
        self.assertFalse(thread.is_alive())
        self.assertEqual(len(errors), 1)
        self.assertIsInstance(errors[0], ValueError)
        s.close()

    @unittest.skipUnless(sys.version_info >= (3, 12), "PEP 688 buffer exporters require Python 3.12")
    def test_publisher_close_during_buffer_export(self):
        p = Publisher(f"export{os.getpid()}", 64)
        exporting, closed = threading.Event(), threading.Event()
        errors = []
        class Exporter:
            def __buffer__(self, flags):
                exporting.set()
                if not closed.wait(2): raise RuntimeError("close stalled")
                return memoryview(b"abc")
        def close():
            if not exporting.wait(2): return
            try: p.close()
            except Exception as error: errors.append(error)
            finally: closed.set()
        thread = threading.Thread(target=close)
        thread.start()
        try:
            with self.assertRaisesRegex(ValueError, "closed"): p.try_send(Exporter())
        finally:
            thread.join(2)
            p.close()
        self.assertFalse(thread.is_alive())
        self.assertEqual(errors, [])

    @unittest.skipUnless(hasattr(os, "fork"), "Unix fork")
    def test_inherited_close_preserves_parent_endpoints(self):
        for endpoint_type in (Publisher, Subscriber):
            with self.subTest(endpoint=endpoint_type.__name__), tempfile.TemporaryDirectory() as path:
                with endpoint_type("fork", 64, path=path) as parent:
                    # Exactly one inherited endpoint makes an erroneous flock
                    # upgrade succeed, reproducing the split-queue failure.
                    leases = list((pathlib.Path(path) / ".cloudtoid/interprocess/v3/readers/fork").iterdir())
                    self.assertEqual(len(leases), 1)
                    child = os.fork()
                    if child == 0:
                        parent.close()
                        os._exit(0)
                    _, status = os.waitpid(child, 0)
                    self.assertEqual(status, 0)
                    self.assertTrue(all(lease.is_file() for lease in leases))
                    if endpoint_type is Publisher:
                        self.assertTrue(parent.try_send(b"parent"))
                        with Subscriber("fork", 64, path=path) as other:
                            self.assertEqual(other.receive(timeout=1), b"parent")
                    else:
                        with Publisher("fork", 64, path=path) as other:
                            self.assertTrue(other.try_send(b"parent"))
                            self.assertEqual(parent.receive(timeout=1), b"parent")

    @unittest.skipUnless(hasattr(signal, "SIGALRM"), "Unix signals")
    def test_signal_handler_can_close_waiting_subscriber(self):
        s = Subscriber(f"signal{os.getpid()}", 64)
        previous = signal.signal(signal.SIGALRM, lambda *_: s.close())
        try:
            signal.setitimer(signal.ITIMER_REAL, 0.02)
            with self.assertRaises(ValueError): s.receive(timeout=1)
        finally:
            signal.setitimer(signal.ITIMER_REAL, 0)
            signal.signal(signal.SIGALRM, previous)
            s.close()

if __name__ == "__main__": unittest.main()
