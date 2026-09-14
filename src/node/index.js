'use strict';
const { existsSync } = require('node:fs');
const { join } = require('node:path');
const { setTimeout: delay } = require('node:timers/promises');
const target = `${process.platform}-${process.arch}`;
const local = join(__dirname, `interprocess.${target}.node`);
const native = existsSync(local) ? require(local) : require(`@cloudtoid/interprocess-${target}`);

class Subscriber {
  #inner;
  #closed = false;

  constructor(name, capacity, path) {
    this.#inner = new native.Subscriber(name, capacity, path);
  }

  tryReceive() {
    if (this.#closed) throw new Error('subscriber is closed');
    return this.#inner.tryReceive();
  }

  async receive(options = {}) {
    if (options === null || typeof options !== 'object') throw new TypeError('receive options must be an object');
    const { signal } = options;
    // Follow AbortSignal's reason, including TimeoutError from AbortSignal.timeout.
    signal?.throwIfAborted();
    while (true) {
      const message = this.tryReceive();
      if (message !== null) return message;
      try {
        await delay(1, undefined, { signal });
      } catch (error) {
        signal?.throwIfAborted();
        throw error;
      }
      signal?.throwIfAborted();
    }
  }

  close() {
    this.#closed = true;
    this.#inner.close();
  }
}

module.exports = { Publisher: native.Publisher, Subscriber };
