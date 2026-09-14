const assert = require('node:assert/strict');
const { Publisher, Subscriber } = require('../../src/node');
const [mode, name, path, count] = process.argv.slice(2);
function message(i) {
  const data = Buffer.alloc(8 + i % 251);
  data.writeBigUInt64LE(BigInt(i));
  for (let j = 8; j < data.length; j++) data[j] = (i + j) % 251;
  return data;
}
async function main() {
  if (mode === 'publish') {
    const p = new Publisher(name, 4096, path);
    try { for (let i = 0; i < +count; i++) { const data = message(i); while (!p.trySend(data)) await new Promise(setImmediate); } }
    finally { p.close(); }
  } else {
    const s = new Subscriber(name, 4096, path);
    console.log('READY');
    try { for (let i = 0; i < +count; i++) assert.deepEqual(await s.receive(30000), message(i)); }
    finally { s.close(); }
  }
}
main().catch(e => { console.error(e); process.exitCode = 1; });
