import os
import unittest
import pathlib
import tempfile
import threading
import time
import signal
from cloudtoid_interprocess import Publisher, Subscriber, CapacityMismatchError

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
                with self.assertRaises(CapacityMismatchError): Publisher("buffers", 128, path=path)
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

    @unittest.skipUnless(hasattr(os, "fork"), "Unix fork")
    def test_inherited_close_preserves_parent_endpoints(self):
        with tempfile.TemporaryDirectory() as path:
            with Publisher("fork", 64, path=path) as p, Subscriber("fork", 64, path=path) as s:
                leases = list(pathlib.Path(path).rglob("readers/*"))
                child = os.fork()
                if child == 0:
                    p.close()
                    s.close()
                    os._exit(0)
                _, status = os.waitpid(child, 0)
                self.assertEqual(status, 0)
                self.assertTrue(all(lease.exists() for lease in leases))
                self.assertTrue(p.try_send(b"parent"))
                with Subscriber("fork", 64, path=path) as other:
                    self.assertEqual(other.receive(timeout=1), b"parent")

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
