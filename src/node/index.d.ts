/// <reference lib="esnext.disposable" />

/** Protocol v3 byte queues. Participants must agree on name, capacity, and Unix path. */
export class Publisher {
  constructor(name: string, capacity: number, path?: string);
  /** False means full or temporarily recovering. Wakeup failures do not change a committed result. */
  trySend(data: Uint8Array): boolean;
  /** Returns the accepted prefix length. A batch is not an atomic transaction. */
  trySendBatch(messages: Uint8Array[]): number;
  close(): void;
  [Symbol.dispose](): void;
}
export class Subscriber {
  constructor(name: string, capacity: number, path?: string);
  /** Null means no ready message. An empty Buffer is a real message. */
  tryReceive(): Buffer | null;
  /** Checks immediately, then retries on a 1 ms timer. Rejects with signal.reason on cancellation. */
  receive(options?: { signal?: AbortSignal }): Promise<Buffer>;
  /** Releases the endpoint. Pending receives reject; closing repeatedly is safe. */
  close(): void;
  [Symbol.dispose](): void;
}
