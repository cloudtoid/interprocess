/** Protocol v3 byte queues. Participants must agree on name, capacity, and Unix path. */
export class Publisher {
  constructor(name: string, capacity: number, path?: string);
  /** False means full or temporarily recovering. Errors may follow a committed send. */
  trySend(data: Buffer): boolean;
  /** Returns the accepted prefix length. A batch is not an atomic transaction. */
  trySendBatch(messages: Buffer[]): number;
  close(): void;
}
export class Subscriber {
  constructor(name: string, capacity: number, path?: string);
  /** Null means no ready message. An empty Buffer is a real message. */
  tryReceive(): Buffer | null;
  /** Runs on a libuv worker. Use finite waits; each pending call occupies one worker. */
  receive(timeoutMs: number): Promise<Buffer | null>;
  /** Pending receives finish within their timeout and retain the queue until then. */
  close(): void;
}
