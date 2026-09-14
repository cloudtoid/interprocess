# Cloudtoid Interprocess for Node.js

Exchange bytes directly with Rust, C, Python, Go, and .NET processes through fast shared-memory queues.

```js
const { Publisher, Subscriber } = require('@cloudtoid/interprocess');
const subscriber = new Subscriber('example', 65536);
const publisher = new Publisher('example', 65536);
try {
  if (publisher.trySend(Buffer.from('hello'))) {
    console.log(subscriber.tryReceive().toString());
  }
} finally {
  subscriber.close();
  publisher.close();
}
```

`trySend` returns false when full or recovering. `trySendBatch` returns the accepted prefix length. `tryReceive` returns a Buffer or null; an empty Buffer is a real message. `await subscriber.receive(timeoutMs)` waits on a libuv worker and requires a finite timeout. Each pending call occupies a worker; use dedicated Worker threads for large numbers of independent blocking subscriptions. Closing prevents new calls; pending receives retain their queue registration until they complete.

Every participant must agree on name, capacity, and Unix path (optional third constructor argument). Capacity is bytes, excludes metadata, exceeds 16, and is divisible by 8. Subscribers compete for messages. Queues are volatile, with recovery for crashed participants; they do not provide durable delivery.

Build from this monorepo with `node build.js`. See the [protocol specification](https://github.com/cloudtoid/interprocess/blob/main/docs/protocol.md).
