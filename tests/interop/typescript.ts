import { Publisher, Subscriber } from '../../src/node';

async function example() {
  using publisher = new Publisher('typescript', 4096);
  using subscriber = new Subscriber('typescript', 4096);
  const accepted: boolean = publisher.trySend(new Uint8Array([1]));
  const count: number = publisher.trySendBatch([Buffer.from('message')]);
  const message: Buffer = await subscriber.receive({signal: AbortSignal.timeout(50)});
  const immediate: Buffer | null = subscriber.tryReceive();
}
