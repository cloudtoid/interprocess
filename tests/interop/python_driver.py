import sys, time
from cloudtoid_interprocess import Publisher, Subscriber
mode, name, path, count = sys.argv[1:]
def message(i):
    return i.to_bytes(8, 'little') + bytes((i + j) % 251 for j in range(8, 8 + i % 251))
if mode == 'publish':
    with Publisher(name, 4096, path) as publisher:
        for i in range(int(count)):
            data = message(i)
            while not publisher.try_send(data): time.sleep(0)
else:
    with Subscriber(name, 4096, path) as subscriber:
        print('READY', flush=True)
        for i in range(int(count)):
            assert subscriber.receive(timeout=30) == message(i), f'message {i} differs'
