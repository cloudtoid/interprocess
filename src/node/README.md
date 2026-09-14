# Cloudtoid Interprocess for Node.js

[API guide](https://cloudtoid.com/docs/node/) · [Queue concepts](https://cloudtoid.com/docs/concepts/) · [Website](https://cloudtoid.com)

Exchange bytes directly with Rust, C, Python, Go, and .NET processes through fast shared-memory queues.

## Install

Run in your Node.js project. Requires Node.js 18 or later; platform binaries install automatically.

```sh
npm install @cloudtoid/interprocess
```

## Example

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

## Build from source

From this directory, run `node build.js`.

Queues are transient: once all publishers and subscribers are gone, unread messages are lost. Keep at least one endpoint connected throughout a handoff between processes. See the [API guide](https://cloudtoid.com/docs/node/) for waiting, errors, ownership, and limits.
