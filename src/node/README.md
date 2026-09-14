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

ES modules can use `import { Publisher, Subscriber } from '@cloudtoid/interprocess'`. CommonJS `require` and TypeScript declarations expose the same classes.

`trySend` returns false when full or recovering. `trySendBatch` returns the accepted prefix length. `tryReceive` returns a Buffer or null; an empty Buffer is a real message.

Send methods accept `Uint8Array` (including `Buffer`).

Build from this monorepo with `node build.js`. See the [protocol specification](https://github.com/cloudtoid/interprocess/blob/main/docs/protocol.md).

## Errors

Errors expose `code`, including `ERR_INVALID_ARGUMENT`, `ERR_CAPACITY_MISMATCH`, `ERR_PUBLISHER_LIMIT`, `ERR_EXHAUSTED`, `ERR_CORRUPT`, `ERR_IO`, and `ERR_CLOSED`. Endpoints support `Symbol.dispose` as an alias for `close`.

## Waiting and cancellation

`await subscriber.receive()` waits for a message. Pass `{ signal }` to cancel, or use `subscriber.receive({ signal: AbortSignal.timeout(1000) })` for a one-second deadline. Cancellation rejects with the signal's reason. `close()` releases the endpoint and makes pending receives reject.

Ready messages use the nonblocking path immediately. Empty queues back off through 1, 2, 4, 8, and 10 ms waits without occupying libuv workers. Each new receive checks immediately and resets the backoff. Idle-to-active delivery can incur up to the 10 ms polling interval, and a busy event loop can delay timers and cancellation. Each pending receive has its own timer; prefer one receive loop per subscriber and use `AbortSignal` to stop unused receives.

## Limits

Every participant must agree on name, capacity, and Unix path (optional third constructor argument). Capacity is bytes, excludes metadata, exceeds 16, and is divisible by 8. Subscribers compete for messages. Queues are volatile, with recovery for crashed participants; they do not provide durable delivery.

Queue names must be nonempty and contain no slash or NUL. Windows also rejects backslashes; Unix permits them for compatibility. The maximum is 24 UTF-8 bytes on macOS and 245 on Linux; use at most 24 bytes for portable names.

Batch sends return the committed prefix length. A short count, including zero, can mean a full queue, recovery, or a mid-batch error. Retry the unsent suffix to observe a persistent error; errors before any commit are raised immediately.

TypeScript declarations require TypeScript 5.2 or newer for `Symbol.dispose`; JavaScript users do not need TypeScript.

## Queue lifetime

The queue is transient: it stays alive while at least one publisher or subscriber is connected. Once all endpoints are closed or their processes exit, unread messages are lost. Opening the same name again creates a fresh, empty queue. Keep a subscriber connected before a short-lived publisher exits; a surviving publisher also keeps the queue alive.
