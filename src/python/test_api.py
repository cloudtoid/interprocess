import os
import unittest
from cloudtoid_interprocess import Publisher, Subscriber

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
        with self.assertRaises(RuntimeError): p.try_send(b"closed")
        with self.assertRaises(RuntimeError): s.try_receive()

if __name__ == "__main__": unittest.main()
