/**
 * Client for the host message bridge.
 *
 * Mirrors `Cayrast.Abstractions.Ipc.IpcEnvelope` on the .NET side. Both ends must
 * agree on this shape exactly — a mismatch produces silent `undefined`s rather than
 * an error, which is a tedious class of bug to track down, so the types here are
 * deliberately explicit rather than inferred.
 */

export type IpcMessageKind = 'Request' | 'Response' | 'Event' | 'Fault';

export type IpcFaultCode =
  | 'Unknown'
  | 'PermissionDenied'
  | 'UnknownChannel'
  | 'InvalidPayload'
  | 'Timeout'
  | 'ModuleUnavailable'
  | 'Cancelled';

export interface IpcFault {
  code: IpcFaultCode;
  message: string;
}

export interface IpcEnvelope<TPayload = unknown> {
  kind: IpcMessageKind;
  id: string;
  channel: string;
  origin?: string | null;
  payload?: TPayload | null;
  fault?: IpcFault | null;
}

/** Thrown when the host answers a request with a fault. */
export class BridgeError extends Error {
  constructor(
    readonly code: IpcFaultCode,
    message: string,
  ) {
    super(message);
    this.name = 'BridgeError';
  }
}

/** How long to wait for a response before giving up. */
const REQUEST_TIMEOUT_MS = 10_000;

interface PendingRequest {
  resolve: (value: unknown) => void;
  reject: (reason: unknown) => void;
  timer: ReturnType<typeof setTimeout>;
}

/** The WebView2 host object, absent when running in a plain browser. */
interface WebViewHost {
  postMessage(message: unknown): void;
  addEventListener(type: 'message', listener: (event: { data: unknown }) => void): void;
}

declare global {
  interface Window {
    chrome?: { webview?: WebViewHost };
  }
}

class Bridge {
  readonly #pending = new Map<string, PendingRequest>();
  readonly #listeners = new Map<string, Set<(payload: unknown) => void>>();
  readonly #host: WebViewHost | undefined;

  constructor() {
    this.#host = window.chrome?.webview;

    if (!this.#host) {
      // Running outside WebView2 — `npm run dev` in a normal browser. The UI stays
      // usable for layout and styling work; only host calls fail, and they fail
      // loudly rather than hanging forever waiting for a reply that cannot come.
      console.warn('[cayrast] No WebView2 host detected. Host calls will fail.');
      return;
    }

    this.#host.addEventListener('message', (event) => this.#receive(event.data));
  }

  /** Whether a real host is present. */
  get connected(): boolean {
    return this.#host !== undefined;
  }

  /**
   * Sends a request and waits for its response.
   *
   * @throws {BridgeError} if the host reports a fault or does not answer in time.
   */
  async request<TResult = unknown>(channel: string, payload?: unknown): Promise<TResult> {
    if (!this.#host) {
      throw new BridgeError('ModuleUnavailable', 'Not running inside the Cayrast host.');
    }

    const id = crypto.randomUUID();

    return new Promise<TResult>((resolve, reject) => {
      // A host that never replies must not leak a promise that is awaited forever;
      // the caller needs a failure it can surface.
      const timer = setTimeout(() => {
        this.#pending.delete(id);
        reject(new BridgeError('Timeout', `The host did not answer '${channel}' in time.`));
      }, REQUEST_TIMEOUT_MS);

      this.#pending.set(id, {
        resolve: resolve as (value: unknown) => void,
        reject,
        timer,
      });

      const envelope: IpcEnvelope = { kind: 'Request', id, channel, payload };
      this.#host!.postMessage(envelope);
    });
  }

  /** Sends a fire-and-forget notification. */
  notify(channel: string, payload?: unknown): void {
    if (!this.#host) {
      return;
    }

    const envelope: IpcEnvelope = {
      kind: 'Event',
      id: crypto.randomUUID(),
      channel,
      payload,
    };

    this.#host.postMessage(envelope);
  }

  /** Subscribes to host-pushed events. Returns an unsubscribe function. */
  on<TPayload = unknown>(channel: string, handler: (payload: TPayload) => void): () => void {
    let handlers = this.#listeners.get(channel);
    if (!handlers) {
      handlers = new Set();
      this.#listeners.set(channel, handlers);
    }

    handlers.add(handler as (payload: unknown) => void);
    return () => handlers.delete(handler as (payload: unknown) => void);
  }

  #receive(raw: unknown): void {
    const envelope = this.#parse(raw);
    if (!envelope) {
      return;
    }

    if (envelope.kind === 'Event') {
      this.#dispatchEvent(envelope);
      return;
    }

    const pending = this.#pending.get(envelope.id);
    if (!pending) {
      // Most likely a response that arrived after its timeout fired.
      return;
    }

    clearTimeout(pending.timer);
    this.#pending.delete(envelope.id);

    if (envelope.kind === 'Fault') {
      const fault = envelope.fault;
      pending.reject(new BridgeError(fault?.code ?? 'Unknown', fault?.message ?? 'The request failed.'));
      return;
    }

    pending.resolve(envelope.payload);
  }

  #parse(raw: unknown): IpcEnvelope | undefined {
    // WebView2 hands over the parsed object for postWebMessageAsJson, but a string
    // arrives when the host used postWebMessageAsString. Accept both.
    if (typeof raw === 'string') {
      try {
        return JSON.parse(raw) as IpcEnvelope;
      } catch {
        console.error('[cayrast] Discarded an unparseable host message.');
        return undefined;
      }
    }

    if (raw && typeof raw === 'object' && 'kind' in raw && 'id' in raw) {
      return raw as IpcEnvelope;
    }

    return undefined;
  }

  #dispatchEvent(envelope: IpcEnvelope): void {
    const handlers = this.#listeners.get(envelope.channel);
    if (!handlers) {
      return;
    }

    for (const handler of handlers) {
      try {
        handler(envelope.payload);
      } catch (error) {
        // One bad subscriber must not stop the others from being notified.
        console.error(`[cayrast] Event handler for '${envelope.channel}' threw.`, error);
      }
    }
  }
}

/** The shared bridge instance. */
export const bridge = new Bridge();
