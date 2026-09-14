const { test } = require('node:test');
const assert = require('node:assert/strict');
const { stat } = require('node:fs/promises');
const { Publisher, Subscriber } = require('./');

test('CommonJS and ES module imports expose the same public classes', async () => {
  const esm = await import('./index.js');
  assert.equal(esm.Publisher, Publisher);
  assert.equal(esm.Subscriber, Subscriber);
});

test('bytes, batch prefix, deadlines, and close', async () => {
  const name = `n${process.pid}`;
  const p = new Publisher(name, 64), s = new Subscriber(name, 64);
  try {
    assert.equal(s.tryReceive(), null);
    assert.equal(p.trySend(Buffer.alloc(0)), true);
    assert.deepEqual(await s.receive(), Buffer.alloc(0));
    assert.equal(p.trySendBatch([Buffer.from('a'), Buffer.from('b')]), 2);
    assert.equal((await s.receive()).toString(), 'a');
    assert.equal((await s.receive()).toString(), 'b');
    await assert.rejects(s.receive({signal: AbortSignal.timeout(5)}), {name: 'TimeoutError'});
    const pending = s.receive();
    s.close();
    await assert.rejects(pending, /closed/);
    assert.throws(() => s.tryReceive(), /closed/);
    await assert.rejects(s.receive(), /closed/);
  } finally { s.close(); p.close(); }
  assert.throws(() => p.trySend(Buffer.alloc(0)), /closed/);
});

test('waiting subscribers leave the worker pool free and deadlines independent', async () => {
  const readers = [], writers = [], held = [];
  const stop = new AbortController();
  let completed = 0;
  try {
    for (let i = 0; i < 8; i++) {
      const name = `nw${process.pid}x${i}`;
      readers.push(new Subscriber(name, 64));
      writers.push(new Publisher(name, 64));
      held.push(readers[i].receive({signal: stop.signal}).then(message => { completed++; return message; }));
    }
    const short = new Subscriber(`nt${process.pid}`, 64);
    try {
      await Promise.all([
        assert.rejects(short.receive({signal: AbortSignal.timeout(10)}), {name: 'TimeoutError'}),
        stat(__filename),
      ]);
      assert.equal(completed, 0);
    } finally { short.close(); }
    for (const writer of writers) assert.equal(writer.trySend(Buffer.from('wake')), true);
    for (const message of await Promise.all(held)) assert.equal(message.toString(), 'wake');
  } finally {
    stop.abort();
    await Promise.allSettled(held);
    readers.forEach(reader => reader.close());
    writers.forEach(writer => writer.close());
  }
});

test('AbortSignal preserves its reason and never consumes on pre-cancelled receive', async () => {
  const name = `na${process.pid}`;
  const p = new Publisher(name, 256), s = new Subscriber(name, 256);
  try {
    for (const invalid of [100, null, true]) await assert.rejects(s.receive(invalid), TypeError);
    const stopped = new AbortController();
    const reason = new Error('application stopped');
    stopped.abort(reason);
    p.trySend(Buffer.from('preserved'));
    await assert.rejects(s.receive({signal: stopped.signal}), error => error === reason);
    assert.equal(s.tryReceive().toString(), 'preserved');
    const controller = new AbortController();
    const cancelled = s.receive({signal: controller.signal});
    controller.abort(reason);
    await assert.rejects(cancelled, error => error === reason);
    const pending = Array.from({length: 8}, () => s.receive());
    for (let i = 0; i < pending.length; i++) assert.equal(p.trySend(Buffer.from([i])), true);
    const received = (await Promise.all(pending)).map(message => message[0]).sort();
    assert.deepEqual(received, [0, 1, 2, 3, 4, 5, 6, 7]);
    const closing = Array.from({length: 3}, () => s.receive());
    s.close(); s.close();
    for (const result of await Promise.allSettled(closing)) {
      assert.equal(result.status, 'rejected');
      assert.match(result.reason.message, /closed/);
    }
    assert.equal(p.trySend(Buffer.from('queue survived')), true);
    const replacement = new Subscriber(name, 256);
    try { assert.equal(replacement.tryReceive().toString(), 'queue survived'); }
    finally { replacement.close(); }
  } finally { s.close(); p.close(); }
});
