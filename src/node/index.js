'use strict';
const { existsSync } = require('node:fs');
const { join } = require('node:path');
const { setTimeout: delay } = require('node:timers/promises');
const target = `${process.platform}-${process.arch}`;
const local = join(__dirname, `interprocess.${target}.node`);
let native;
try {
  native = existsSync(local) ? require(local) : require(`@cloudtoid/interprocess-${target}`);
} catch (cause) {
  throw Object.assign(new Error(`Cannot load Cloudtoid Interprocess for ${target}. Install its optional platform package or build from source; Linux binaries require glibc 2.34+.`, { cause }), { code: 'ERR_NATIVE_UNAVAILABLE' });
}

class Subscriber {
  #inner;
  #closed = false;

  constructor(name, capacity, path) {
    this.#inner = new native.Subscriber(name, capacity, path);
  }

  tryReceive() {
    if (this.#closed) throw Object.assign(new Error('subscriber is closed'), { code: 'ERR_CLOSED' });
    return this.#inner.tryReceive();
  }

  async receive(options = {}) {
    if (options === null || typeof options !== 'object') throw new TypeError('receive options must be an object');
    const { signal } = options;
    // Follow AbortSignal's reason, including TimeoutError from AbortSignal.timeout.
    signal?.throwIfAborted();
    let waitMs = 1;
    while (true) {
      const message = this.tryReceive();
      if (message !== null) return message;
      try {
        await delay(waitMs, undefined, { signal });
      } catch (error) {
        signal?.throwIfAborted();
        throw error;
      }
      signal?.throwIfAborted();
      waitMs = Math.min(waitMs * 2, 10);
    }
  }

  close() {
    this.#closed = true;
    this.#inner.close();
  }
}

const dispose = Symbol.dispose ?? Symbol.for('nodejs.dispose');
native.Publisher.prototype[dispose] = native.Publisher.prototype.close;
Subscriber.prototype[dispose] = Subscriber.prototype.close;
exports.Publisher = native.Publisher;
exports.Subscriber = Subscriber;
