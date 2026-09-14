import sys, time, os
from cloudtoid_interprocess import Publisher, Subscriber
mode, name, path, count = sys.argv[1:5]
capacity = int(os.environ.get("INTEROP_CAPACITY", "4096"))
def message(i):
    return i.to_bytes(8, 'little') + bytes((i + j) % 251 for j in range(8, 4088 if i % 251 == 250 else 8 + i % 251))
if mode == 'publish':
    with Publisher(name, capacity, path) as publisher:
        start = int(sys.argv[5]) if len(sys.argv) > 5 else 0
        if len(sys.argv) > 5:
            print('READY', flush=True)
            input()
        for i in range(start, start + int(count)):
            data = message(i)
            while not publisher.try_send(data): time.sleep(0)
else:
    with Subscriber(name, capacity, path) as subscriber:
        print('READY', flush=True)
        if mode == 'collect':
            while True:
                data = subscriber.receive(timeout=30)
                assert data is not None, 'collector timed out'
                if not data: break
                i = int.from_bytes(data[:8], 'little')
                assert data == message(i), f'message {i} differs'
                print(i, flush=True)
            sys.exit(0)
        for i in range(int(count)):
            assert subscriber.receive(timeout=30) == message(i), f'message {i} differs'
