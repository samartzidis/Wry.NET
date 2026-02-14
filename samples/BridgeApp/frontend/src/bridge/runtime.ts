/**
 * Wry Bridge Runtime
 *
 * Provides typed RPC-style communication between the React frontend
 * and .NET backend services via wry's IPC message channel.
 *
 * Protocol:
 *   JS -> .NET (call):   { callId, method, args }
 *   JS -> .NET (cancel): { cancel: callId }
 *   .NET -> JS (reply):  { callId, result } or { callId, error: { message, type } }
 *   .NET -> JS (event):  { event: "name", data: ... }
 */

interface BridgeRequest {
  callId: string;
  method: string;
  args: unknown[];
}

interface BridgeCancelRequest {
  cancel: string;
}

interface BridgeResponse {
  callId: string;
  result?: unknown;
  error?: { message: string; type: string };
}

interface BridgeEventMessage {
  event: string;
  data?: unknown;
}

interface PendingCall {
  resolve: (value: unknown) => void;
  reject: (error: Error) => void;
  timer?: ReturnType<typeof setTimeout>;
  method: string;
}

/** Options that can be passed to individual calls. */
export interface CallOptions {
  /** Timeout in milliseconds. Overrides the default timeout for this call.
   *  Set to 0 to disable timeout for this specific call. */
  timeoutMs?: number;
}

/** Map of in-flight calls awaiting responses, keyed by callId. */
const pending = new Map<string, PendingCall>();

let initialized = false;
let callCounter = 0;

/**
 * Default timeout for bridge calls in milliseconds.
 * Calls that don't receive a response within this time will be rejected
 * with a BridgeTimeoutError and a cancel message is sent to .NET.
 * Set to 0 to disable (not recommended).
 * @default 30000 (30 seconds)
 */
let defaultTimeoutMs = 30_000;

/**
 * Configure the default timeout for all bridge calls.
 * @param ms - Timeout in milliseconds. 0 disables timeouts (not recommended).
 */
export function setDefaultTimeout(ms: number): void {
  defaultTimeoutMs = ms;
}

/** Returns the current default timeout in milliseconds. */
export function getDefaultTimeout(): number {
  return defaultTimeoutMs;
}

function generateCallId(): string {
  return `c_${++callCounter}_${Date.now()}`;
}

// eslint-disable-next-line @typescript-eslint/no-explicit-any
function sendRaw(msg: any): void {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (window as any).external.sendMessage(JSON.stringify(msg));
}

/**
 * Send a cancel request to .NET for an in-flight call.
 * This triggers CancellationToken cancellation on the C# side.
 */
function sendCancel(callId: string): void {
  const msg: BridgeCancelRequest = { cancel: callId };
  sendRaw(msg);
}

/**
 * Custom error class for bridge call timeouts.
 */
export class BridgeTimeoutError extends Error {
  public readonly callId: string;
  public readonly method: string;

  constructor(callId: string, method: string, timeoutMs: number) {
    super(
      `Bridge call '${method}' (${callId}) timed out after ${timeoutMs}ms`
    );
    this.name = "BridgeTimeoutError";
    this.callId = callId;
    this.method = method;
  }
}

/**
 * Custom error class for cancelled bridge calls.
 */
export class BridgeCancelledError extends Error {
  public readonly callId: string;
  public readonly method: string;

  constructor(callId: string, method: string) {
    super(`Bridge call '${method}' (${callId}) was cancelled`);
    this.name = "BridgeCancelledError";
    this.callId = callId;
    this.method = method;
  }
}

// ---------------------------------------------------------------------------
// Event System — .NET → JS push notifications
// ---------------------------------------------------------------------------

/** Callback type for event listeners. */
export type EventCallback<T = unknown> = (data: T) => void;

/** Map of event listeners, keyed by event name. */
const eventListeners = new Map<string, Set<EventCallback>>();

/**
 * Subscribe to a .NET event.
 *
 * @param eventName - The event name (matches the C# `bridge.Emit("name", data)` call)
 * @param callback - Called each time the event fires, with the deserialized payload
 * @returns A dispose function that removes this listener
 *
 * @example
 * ```ts
 * const unsub = on("progress", (data) => console.log(data));
 * // later: unsub();
 * ```
 */
export function on<T = unknown>(
  eventName: string,
  callback: EventCallback<T>
): () => void {
  ensureInitialized();

  let listeners = eventListeners.get(eventName);
  if (!listeners) {
    listeners = new Set();
    eventListeners.set(eventName, listeners);
  }
  listeners.add(callback as EventCallback);

  return () => off(eventName, callback);
}

/**
 * Unsubscribe a specific callback from an event.
 */
export function off<T = unknown>(
  eventName: string,
  callback: EventCallback<T>
): void {
  const listeners = eventListeners.get(eventName);
  if (listeners) {
    listeners.delete(callback as EventCallback);
    if (listeners.size === 0) {
      eventListeners.delete(eventName);
    }
  }
}

/**
 * Subscribe to a .NET event for a single occurrence.
 * The listener is automatically removed after the first call.
 *
 * @returns A dispose function that removes this listener (if it hasn't fired yet)
 */
export function once<T = unknown>(
  eventName: string,
  callback: EventCallback<T>
): () => void {
  const wrapper: EventCallback = (data) => {
    off(eventName, wrapper);
    (callback as EventCallback)(data);
  };
  return on(eventName, wrapper);
}

/**
 * Dispatch an event to all registered listeners.
 */
function dispatchEvent(eventName: string, data: unknown): void {
  const listeners = eventListeners.get(eventName);
  if (!listeners || listeners.size === 0) return;

  // Iterate a copy so listeners can safely unsubscribe during dispatch
  for (const cb of [...listeners]) {
    try {
      cb(data);
    } catch (err) {
      console.error(
        `[Bridge] Error in event listener for '${eventName}':`,
        err
      );
    }
  }
}

// ---------------------------------------------------------------------------
// Initialization
// ---------------------------------------------------------------------------

/**
 * Lazily initialize the single receiveMessage callback.
 * Routes incoming messages to either the event system or the RPC pending map.
 */
function ensureInitialized(): void {
  if (initialized) return;
  initialized = true;

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  (window as any).external.receiveMessage((raw: string) => {
    let msg: BridgeResponse & BridgeEventMessage;
    try {
      msg = JSON.parse(raw);
    } catch {
      // Not a bridge message -- ignore
      return;
    }

    // Event message: { event: "name", data: ... }
    if (msg.event) {
      dispatchEvent(msg.event, msg.data);
      return;
    }

    // RPC response: { callId, result/error }
    if (!msg.callId) return;

    const entry = pending.get(msg.callId);
    if (!entry) return;

    // Clear the timeout timer if one was set
    if (entry.timer !== undefined) {
      clearTimeout(entry.timer);
    }

    pending.delete(msg.callId);

    if (msg.error) {
      const err = new Error(msg.error.message);
      err.name = msg.error.type;
      entry.reject(err);
    } else {
      entry.resolve(msg.result);
    }
  });
}

/**
 * Cancel an in-flight call by its callId.
 * Rejects the pending promise with a BridgeCancelledError and sends
 * a cancel message to .NET to trigger CancellationToken cancellation.
 *
 * @param callId - The call ID returned by callWithId or available on CancellablePromise.callId
 * @returns true if the call was found and cancelled, false if it was already completed/cancelled
 */
export function cancelCall(callId: string): boolean {
  const entry = pending.get(callId);
  if (!entry) return false;

  if (entry.timer !== undefined) {
    clearTimeout(entry.timer);
  }

  pending.delete(callId);
  entry.reject(new BridgeCancelledError(callId, entry.method));

  // Tell .NET to cancel the in-flight call
  sendCancel(callId);

  return true;
}

/**
 * Call a .NET service method and return a typed promise.
 *
 * @param method - Fully qualified method name: "ServiceName.MethodName"
 * @param args - Positional arguments matching the C# method signature
 * @returns A promise that resolves with the deserialized return value
 *
 * @example
 * ```ts
 * const greeting = await call<string>("GreetService.Greet", "World");
 * ```
 */
export function call<T>(method: string, ...args: unknown[]): Promise<T> {
  return callWithOptions<T>({}, method, ...args);
}

/**
 * Call a .NET service method with per-call options (e.g. custom timeout).
 *
 * @param options - Call options (timeoutMs, etc.)
 * @param method - Fully qualified method name: "ServiceName.MethodName"
 * @param args - Positional arguments matching the C# method signature
 * @returns A promise that resolves with the deserialized return value
 */
export function callWithOptions<T>(
  options: CallOptions,
  method: string,
  ...args: unknown[]
): Promise<T> {
  ensureInitialized();

  const callId = generateCallId();
  const timeoutMs = options.timeoutMs ?? defaultTimeoutMs;

  return new Promise<T>((resolve, reject) => {
    const entry: PendingCall = {
      resolve: resolve as (value: unknown) => void,
      reject,
      method,
    };

    // Set up timeout if configured — also sends cancel to .NET
    if (timeoutMs > 0) {
      entry.timer = setTimeout(() => {
        if (pending.delete(callId)) {
          reject(new BridgeTimeoutError(callId, method, timeoutMs));
          // Tell .NET to cancel the timed-out call
          sendCancel(callId);
        }
      }, timeoutMs);
    }

    pending.set(callId, entry);

    const request: BridgeRequest = { callId, method, args };
    sendRaw(request);
  });
}

/**
 * A promise wrapper that supports cancellation.
 * The callId is exposed so it can be passed to cancelCall().
 *
 * @example
 * ```ts
 * const p = cancellableCall<string>("GreetService.SlowMethod", 30);
 * // Later:
 * p.cancel(); // cancels both JS promise and .NET CancellationToken
 * ```
 */
export class CancellablePromise<T> implements PromiseLike<T> {
  public readonly callId: string;
  private readonly _promise: Promise<T>;

  constructor(options: CallOptions, method: string, ...args: unknown[]) {
    ensureInitialized();

    this.callId = generateCallId();
    const callId = this.callId;
    const timeoutMs = options.timeoutMs ?? defaultTimeoutMs;

    this._promise = new Promise<T>((resolve, reject) => {
      const entry: PendingCall = {
        resolve: resolve as (value: unknown) => void,
        reject,
        method,
      };

      if (timeoutMs > 0) {
        entry.timer = setTimeout(() => {
          if (pending.delete(callId)) {
            reject(new BridgeTimeoutError(callId, method, timeoutMs));
            sendCancel(callId);
          }
        }, timeoutMs);
      }

      pending.set(callId, entry);

      const request: BridgeRequest = { callId, method, args };
      sendRaw(request);
    });
  }

  /** Cancel this call. Rejects the promise and tells .NET to cancel. */
  cancel(): boolean {
    return cancelCall(this.callId);
  }

  then<TResult1 = T, TResult2 = never>(
    onfulfilled?:
      | ((value: T) => TResult1 | PromiseLike<TResult1>)
      | null,
    onrejected?:
      | ((reason: unknown) => TResult2 | PromiseLike<TResult2>)
      | null
  ): Promise<TResult1 | TResult2> {
    return this._promise.then(onfulfilled, onrejected);
  }

  catch<TResult = never>(
    onrejected?:
      | ((reason: unknown) => TResult | PromiseLike<TResult>)
      | null
  ): Promise<T | TResult> {
    return this._promise.catch(onrejected);
  }

  finally(onfinally?: (() => void) | null): Promise<T> {
    return this._promise.finally(onfinally);
  }
}

/**
 * Call a .NET service method returning a CancellablePromise.
 *
 * @example
 * ```ts
 * const p = cancellableCall<string>("GreetService.SlowMethod", 30);
 * setTimeout(() => p.cancel(), 2000); // cancel after 2s
 * try { await p; } catch (e) { if (e instanceof BridgeCancelledError) ... }
 * ```
 */
export function cancellableCall<T>(
  method: string,
  ...args: unknown[]
): CancellablePromise<T> {
  return new CancellablePromise<T>({}, method, ...args);
}

/**
 * Returns the number of calls currently awaiting a response.
 * Useful for debugging or health checks.
 */
export function getPendingCallCount(): number {
  return pending.size;
}
