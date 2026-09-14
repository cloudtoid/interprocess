const { test } = require('node:test');
const assert = require('node:assert/strict');
const { Publisher, Subscriber } = require('./');
test('bytes, batch prefix, timeout, async lifetime, and close', async () => {
  const name = `n${process.pid}`;
  const p = new Publisher(name, 64), s = new Subscriber(name, 64);
  try {
    assert.equal(s.tryReceive(), null);
    assert.equal(p.trySend(Buffer.alloc(0)), true);
    assert.deepEqual(s.tryReceive(), Buffer.alloc(0));
    assert.equal(p.trySendBatch([Buffer.from('a'), Buffer.from('b')]), 2);
    assert.equal((await s.receive(100)).toString(), 'a');
    assert.equal((await s.receive(100)).toString(), 'b');
    assert.equal(await s.receive(5), null);
    const pending = s.receive(100);
    s.close();
    assert.equal(p.trySend(Buffer.from('alive')), true);
    assert.equal((await pending).toString(), 'alive');
    assert.throws(() => s.tryReceive(), /closed/);
  } finally { s.close(); p.close(); }
  assert.throws(() => p.trySend(Buffer.alloc(0)), /closed/);
});
