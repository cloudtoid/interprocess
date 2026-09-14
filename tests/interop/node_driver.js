const assert = require('node:assert/strict');
const { Publisher, Subscriber } = require('../../src/node');
const [mode, name, path, count, start] = process.argv.slice(2);
function message(i) {
  const data = Buffer.alloc(8 + i % 251);
  data.writeBigUInt64LE(BigInt(i));
  for (let j = 8; j < data.length; j++) data[j] = (i + j) % 251;
  return data;
}
async function main() {
  if (mode === 'publish') {
    const p = new Publisher(name, 4096, path);
    try {
      if (start !== undefined) {
        console.log('READY');
        await new Promise(resolve => process.stdin.once('data', resolve));
        process.stdin.pause();
      }
      for (let i = +(start || 0); i < +(start || 0) + +count; i++) {
        const data = message(i);
        while (!p.trySend(data)) await new Promise(setImmediate);
      }
    } finally { p.close(); }
  } else {
    const s = new Subscriber(name, 4096, path);
    const signal = AbortSignal.timeout(30000);
    console.log('READY');
    try {
      if (mode === 'collect') {
        while (true) {
          const data = await s.receive({signal});
          if (!data.length) break;
          const id = Number(data.readBigUInt64LE());
          assert.deepEqual(data, message(id));
          console.log(id);
        }
      } else {
        for (let i = 0; i < +count; i++) assert.deepEqual(await s.receive({signal}), message(i));
      }
    } finally { s.close(); }
  }
}
main().catch(e => { console.error(e); process.exitCode = 1; });
