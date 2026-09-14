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

test('typed arrays, error codes, and disposal', () => {
  const name = `types${process.pid}`;
  const p = new Publisher(name, 64), s = new Subscriber(name, 64);
  const dispose = Symbol.dispose ?? Symbol.for('nodejs.dispose');
  try {
    assert.equal(p.trySend(new Uint8Array([1, 2])), true);
    assert.deepEqual(s.tryReceive(), Buffer.from([1, 2]));
    assert.equal(p.trySendBatch([new Uint8Array([3]), new Uint8Array([4])]), 2);
    assert.deepEqual(s.tryReceive(), Buffer.from([3]));
    assert.deepEqual(s.tryReceive(), Buffer.from([4]));
    assert.throws(() => new Publisher(name, 128), {code: 'ERR_CAPACITY_MISMATCH'});
    for (const capacity of [-1, NaN, Infinity, 64.5, Number.MAX_SAFE_INTEGER + 1]) {
      assert.throws(() => new Publisher(name, capacity), {code: 'ERR_INVALID_ARGUMENT'});
    }
  } finally { p[dispose](); s[dispose](); }
  assert.throws(() => p.trySend(new Uint8Array()), {code: 'ERR_CLOSED'});
  assert.throws(() => s.tryReceive(), {code: 'ERR_CLOSED'});
});

test('native endpoints survive repeated process and worker teardown', async () => {
  const { spawnSync } = require('node:child_process');
  const { Worker } = require('node:worker_threads');
  const modulePath = __dirname;
  const script = `
    const {Publisher, Subscriber} = require(${JSON.stringify(modulePath)});
    const p = new Publisher('exit'+process.pid, 64), s = new Subscriber('exit'+process.pid, 64);
    for (let i=0; i<5000; i++) {
      if (!p.trySend(new Uint8Array([1,2,3])) || s.tryReceive().length !== 3) throw Error('delivery');
    }
    // Leave endpoints for environment cleanup, as an exiting application may.
  `;
  for (let i = 0; i < 12; i++) {
    const child = spawnSync(process.execPath, ['-e', script], {encoding: 'utf8', timeout: 10000});
    assert.equal(child.status, 0, child.stderr || String(child.error));
  }
  const name = `worker${process.pid}`, subscriber = new Subscriber(name, 64);
  try {
    const workers = Array.from({length: 4}, (_, id) => new Worker(`
      const {workerData} = require('node:worker_threads');
      const {Publisher} = require(workerData.modulePath);
      const publisher = new Publisher(workerData.name, 64);
      if (!publisher.trySend(new Uint8Array([workerData.id]))) throw Error('send');
    `, {eval: true, workerData: {name, id, modulePath}}));
    const exits = workers.map(worker => new Promise((resolve, reject) => {
      worker.once('error', reject);
      worker.once('exit', code => code === 0 ? resolve() : reject(Error(`worker exit ${code}`)));
    }));
    const messages = [];
    for (let i = 0; i < 4; i++) messages.push((await subscriber.receive({signal: AbortSignal.timeout(10000)}))[0]);
    await Promise.all(exits);
    assert.deepEqual(messages.sort(), [0,1,2,3]);
  } finally { subscriber.close(); }
});
