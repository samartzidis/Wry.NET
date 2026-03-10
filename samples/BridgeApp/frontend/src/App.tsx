import { useState, useCallback, useRef, useEffect } from "react";
import { BackendService, DialogService, TrayService, onProgress, onChildButtonPressed, onTrayMenuItemClicked, onTrayIconEvent, onWindowFocusChanged, onWindowResized, WryDialogKind, WryDialogButtons } from "./bindings";
import type { Person, Employee, ProgressEvent, TrayIconEventPayload } from "./bindings";
import {
  callWithOptions,
  BridgeTimeoutError,
  BridgeCancelledError,
  CancellablePromise,
} from "./bindings/runtime";
import "./App.css";

function App() {
  const [greeting, setGreeting] = useState<string>("");
  const [person, setPerson] = useState<Person | null>(null);
  const [sum, setSum] = useState<number | null>(null);
  const [asyncMsg, setAsyncMsg] = useState<string>("");
  const [people, setPeople] = useState<Person[]>([]);
  const [employee, setEmployee] = useState<Employee | null>(null);
  const [bytesResult, setBytesResult] = useState<string>("");
  const [valueTaskResult, setValueTaskResult] = useState<string>("");
  const [timeoutResult, setTimeoutResult] = useState<string>("");
  const [cancelResult, setCancelResult] = useState<string>("");
  const [progressPercent, setProgressPercent] = useState<number>(0);
  const [progressMsg, setProgressMsg] = useState<string>("");
  const [progressResult, setProgressResult] = useState<string>("");
  const [error, setError] = useState<string>("");
  const [dialogResult, setDialogResult] = useState<string>("");
  const [childMessage, setChildMessage] = useState<string>("");
  const [trayIds, setTrayIds] = useState<number[]>([]);
  const [lastTrayMenuItemClicked, setLastTrayMenuItemClicked] = useState<{ trayId: number; itemId: string } | null>(null);
  const [lastTrayIconEvent, setLastTrayIconEvent] = useState<TrayIconEventPayload | null>(null);
  const [trayTooltipInput, setTrayTooltipInput] = useState<string>("");
  const [trayError, setTrayError] = useState<string>("");
  const [lastWindowFocus, setLastWindowFocus] = useState<{ windowId: number; focused: boolean } | null>(null);
  const [lastWindowResize, setLastWindowResize] = useState<{ windowId: number; width: number; height: number } | null>(null);

  // Ref to hold the cancellable promise for the cancellation test
  const cancelRef = useRef<CancellablePromise<string> | null>(null);

  // Subscribe to progress events (typed, auto-generated helper)
  useEffect(() => {
    const unsub = onProgress((data: ProgressEvent) => {
      setProgressPercent(data.percent);
      setProgressMsg(data.message);
    });
    return unsub;
  }, []);

  // Subscribe to child window button presses (cross-window communication demo)
  useEffect(() => {
    const unsub = onChildButtonPressed((data) => {
      setChildMessage(`${data.message} at ${data.time}`);
    });
    return unsub;
  }, []);

  // Load tray IDs on mount; subscribe to tray menu and icon events
  useEffect(() => {
    TrayService.GetTrayIds().then((ids) => setTrayIds(ids)).catch(() => setTrayIds([]));
    const unsubMenu = onTrayMenuItemClicked((data) => {
      setLastTrayMenuItemClicked({ trayId: data.trayId, itemId: data.itemId });
    });
    const unsubIcon = onTrayIconEvent((data) => {
      setLastTrayIconEvent(data);
    });
    return () => {
      unsubMenu();
      unsubIcon();
    };
  }, []);

  // Subscribe to window focus and resize events (demo)
  useEffect(() => {
    const unsubFocus = onWindowFocusChanged((data) => {
      setLastWindowFocus({ windowId: data.windowId, focused: data.focused });
    });
    const unsubResize = onWindowResized((data) => {
      setLastWindowResize({ windowId: data.windowId, width: data.width, height: data.height });
    });
    return () => {
      unsubFocus();
      unsubResize();
    };
  }, []);

  const handleGreet = useCallback(async () => {
    try {
      setError("");
      const result = await BackendService.Greet("World");
      setGreeting(result);
    } catch (e) {
      setError((e as Error).message);
    }
  }, []);

  const handleGetPerson = useCallback(async () => {
    try {
      setError("");
      const result = await BackendService.GetPerson("Alice", 30);
      setPerson(result);
    } catch (e) {
      setError((e as Error).message);
    }
  }, []);

  const handleAdd = useCallback(async () => {
    try {
      setError("");
      const result = await BackendService.Add(17, 25);
      setSum(result);
    } catch (e) {
      setError((e as Error).message);
    }
  }, []);

  const handleAsync = useCallback(async () => {
    try {
      setError("");
      setAsyncMsg("Waiting...");
      const result = await BackendService.GetGreetingAsync("TypeScript");
      setAsyncMsg(result);
    } catch (e) {
      setError((e as Error).message);
    }
  }, []);

  const handleGetPeople = useCallback(async () => {
    try {
      setError("");
      const result = await BackendService.GetPeople();
      setPeople(result);
    } catch (e) {
      setError((e as Error).message);
    }
  }, []);

  const handleGetEmployee = useCallback(async () => {
    try {
      setError("");
      const result = await BackendService.GetEmployee("Dana", "Engineering");
      setEmployee(result);
    } catch (e) {
      setError((e as Error).message);
    }
  }, []);

  // --- Feature test methods ---

  const handleEchoBytes = useCallback(async () => {
    try {
      setError("");
      // Send base64-encoded bytes "Hello" = SGVsbG8=
      const input = btoa("Hello");
      const result = await BackendService.EchoBytes(input);
      // result is base64 of the reversed bytes
      const decoded = atob(result);
      setBytesResult(
        `Sent: "${input}" (base64 of "Hello") → Got: "${result}" (base64) → Decoded: "${decoded}"`
      );
    } catch (e) {
      setError((e as Error).message);
    }
  }, []);

  const handleValueTask = useCallback(async () => {
    try {
      setError("");
      setValueTaskResult("Waiting...");
      const result = await BackendService.GetValueTaskGreeting("TypeScript");
      setValueTaskResult(result);
    } catch (e) {
      setError((e as Error).message);
    }
  }, []);

  const handleTimeout = useCallback(async () => {
    try {
      setError("");
      setTimeoutResult("Calling SlowMethod(60) with 3s timeout...");
      // Call a method that takes 60 seconds, but with a 3-second timeout
      const result = await callWithOptions<string>(
        { timeoutMs: 3000 },
        "BackendService.SlowMethod",
        60
      );
      setTimeoutResult(result);
    } catch (e) {
      if (e instanceof BridgeTimeoutError) {
        setTimeoutResult(
          `Timeout triggered as expected: ${e.message}`
        );
      } else {
        setError((e as Error).message);
      }
    }
  }, []);

  const handleCancelStart = useCallback(async () => {
    try {
      setError("");
      setCancelResult("Started SlowMethod(60) — click Cancel to abort...");
      const p = new CancellablePromise<string>(
        { timeoutMs: 0 }, // no auto-timeout, manual cancel only
        "BackendService.SlowMethod",
        60
      );
      cancelRef.current = p;
      const result = await p;
      setCancelResult(`Completed (unexpected): ${result}`);
    } catch (e) {
      if (e instanceof BridgeCancelledError) {
        setCancelResult(
          `Cancelled as expected: ${e.message}`
        );
      } else {
        setError((e as Error).message);
      }
    } finally {
      cancelRef.current = null;
    }
  }, []);

  const handleCancelAbort = useCallback(() => {
    if (cancelRef.current) {
      cancelRef.current.cancel();
    }
  }, []);

  const handleProgress = useCallback(async () => {
    try {
      setError("");
      setProgressPercent(0);
      setProgressMsg("Starting...");
      setProgressResult("");
      const result = await BackendService.RunWithProgress(6);
      setProgressResult(result);
    } catch (e) {
      setError((e as Error).message);
    }
  }, []);

  // --- Native dialog demos ---
  const handleMessage = useCallback(async () => {
    try {
      setError("");
      setDialogResult("");
      const button = await DialogService.Message(
        "This is a message dialog.",
        "Demo",
        WryDialogKind.Info,
        WryDialogButtons.OkCancel
      );
      setDialogResult(`Message: you clicked "${button ?? "?"}"`);
    } catch (e) {
      setError((e as Error).message);
    }
  }, []);

  const handleAsk = useCallback(async () => {
    try {
      setError("");
      setDialogResult("");
      const yes = await DialogService.Ask("Do you want to continue?", "Yes/No", WryDialogKind.Info);
      setDialogResult(`Ask: you chose ${yes ? "Yes" : "No"}`);
    } catch (e) {
      setError((e as Error).message);
    }
  }, []);

  const handleConfirm = useCallback(async () => {
    try {
      setError("");
      setDialogResult("");
      const ok = await DialogService.Confirm("Save changes before closing?", "Confirm", WryDialogKind.Warning);
      setDialogResult(`Confirm: you chose ${ok ? "Ok" : "Cancel"}`);
    } catch (e) {
      setError((e as Error).message);
    }
  }, []);

  const handleOpenFile = useCallback(async () => {
    try {
      setError("");
      setDialogResult("");
      const paths = await DialogService.Open(
        false,
        true,
        "Select file(s)",
        "",
        "Images",
        "png,jpg,gif,webp"
      );
      if (paths && paths.length > 0) {
        setDialogResult(`Open: ${paths.length} file(s) — ${paths.slice(0, 3).join(", ")}${paths.length > 3 ? "…" : ""}`);
      } else {
        setDialogResult("Open: cancelled");
      }
    } catch (e) {
      setError((e as Error).message);
    }
  }, []);

  const handleOpenFolder = useCallback(async () => {
    try {
      setError("");
      setDialogResult("");
      const paths = await DialogService.Open(true, false, "Select folder", "", "", "");
      if (paths && paths.length > 0) {
        setDialogResult(`Open folder: ${paths[0]}`);
      } else {
        setDialogResult("Open folder: cancelled");
      }
    } catch (e) {
      setError((e as Error).message);
    }
  }, []);

  const handleSave = useCallback(async () => {
    try {
      setError("");
      setDialogResult("");
      const path = await DialogService.Save("Save file", "", "Text", "txt");
      if (path) {
        setDialogResult(`Save: ${path}`);
      } else {
        setDialogResult("Save: cancelled");
      }
    } catch (e) {
      setError((e as Error).message);
    }
  }, []);

  // --- Tray (TrayService) ---
  const firstTrayId = trayIds.length > 0 ? trayIds[0] : null;
  const handleTrayRefreshIds = useCallback(async () => {
    setTrayError("");
    try {
      const ids = await TrayService.GetTrayIds();
      setTrayIds(ids);
    } catch (e) {
      setTrayError((e as Error).message);
    }
  }, []);
  const handleSetTrayTooltip = useCallback(async () => {
    if (firstTrayId == null) return;
    setTrayError("");
    try {
      await TrayService.SetTooltip(firstTrayId, trayTooltipInput);
    } catch (e) {
      setTrayError((e as Error).message);
    }
  }, [firstTrayId, trayTooltipInput]);
  const handleSetTrayVisible = useCallback(
    async (visible: boolean) => {
      if (firstTrayId == null) return;
      setTrayError("");
      try {
        await TrayService.SetVisible(firstTrayId, visible);
      } catch (e) {
        setTrayError((e as Error).message);
      }
    },
    [firstTrayId]
  );
  const handleSetMenuOnLeftClick = useCallback(
    async (menuOnLeftClick: boolean) => {
      if (firstTrayId == null) return;
      setTrayError("");
      try {
        await TrayService.SetMenuOnLeftClick(firstTrayId, menuOnLeftClick);
      } catch (e) {
        setTrayError((e as Error).message);
      }
    },
    [firstTrayId]
  );
  const handleAppendTrayItemFromJs = useCallback(async () => {
    if (firstTrayId == null) return;
    setTrayError("");
    try {
      await TrayService.AppendMenuItem(firstTrayId, "", "js-demo", "Added from JS", true);
    } catch (e) {
      setTrayError((e as Error).message);
    }
  }, [firstTrayId]);
  const handleSetShowWindowLabel = useCallback(async () => {
    if (firstTrayId == null) return;
    setTrayError("");
    try {
      await TrayService.SetMenuItemText(firstTrayId, "show", "Show Window (from JS)");
    } catch (e) {
      setTrayError((e as Error).message);
    }
  }, [firstTrayId]);
  const handleRemoveJsDemoItem = useCallback(async () => {
    if (firstTrayId == null) return;
    setTrayError("");
    try {
      await TrayService.RemoveMenuItem(firstTrayId, "js-demo");
    } catch (e) {
      setTrayError((e as Error).message);
    }
  }, [firstTrayId]);
  const trayIconInputRef = useRef<HTMLInputElement>(null);
  const handleSetTrayIconFromFile = useCallback(
    async (e: React.ChangeEvent<HTMLInputElement>) => {
      if (firstTrayId == null || !e.target.files?.length) return;
      const file = e.target.files[0];
      setTrayError("");
      try {
        const base64 = await new Promise<string>((resolve, reject) => {
          const r = new FileReader();
          r.onload = () => {
            const dataUrl = r.result as string;
            const base64 = dataUrl.replace(/^data:[^;]+;base64,/, "");
            resolve(base64);
          };
          r.onerror = () => reject(r.error);
          r.readAsDataURL(file);
        });
        await TrayService.SetIconFromBase64(firstTrayId, base64);
      } catch (err) {
        setTrayError((err as Error).message);
      }
      e.target.value = "";
    },
    [firstTrayId]
  );

  return (
    <div className="container">
      <img src="app.png" alt="Wry.NET" className="app-logo" />
      <h1>Wry.NET</h1>
      <p className="description">
        Typed RPC calls from React/TypeScript to .NET via auto-generated
        bindings.
      </p>

      {childMessage && (
        <div className="result-item" style={{ marginBottom: "1rem", padding: "0.5rem", background: "rgba(100,108,255,0.1)", borderRadius: 4 }}>
          <strong>From child window:</strong> {childMessage}
        </div>
      )}

      {(lastWindowFocus != null || lastWindowResize != null) && (
        <div className="result-item" style={{ marginBottom: "1rem", padding: "0.5rem", background: "rgba(80,180,120,0.1)", borderRadius: 4 }}>
          <strong>Window events:</strong>
          {lastWindowFocus != null && (
            <div style={{ marginTop: 4 }}>
              Focus: window {lastWindowFocus.windowId} {lastWindowFocus.focused ? "gained focus" : "lost focus"}
            </div>
          )}
          {lastWindowResize != null && (
            <div style={{ marginTop: 4 }}>
              Resize: window {lastWindowResize.windowId} → {lastWindowResize.width}×{lastWindowResize.height}
            </div>
          )}
        </div>
      )}

      {error && <div className="error">{error}</div>}

      <h2 className="section-title">Basic Calls</h2>
      <div className="actions">
        <button className="primary-btn" onClick={handleGreet}>
          Greet("World")
        </button>
        <button className="primary-btn" onClick={handleGetPerson}>
          GetPerson("Alice", 30)
        </button>
        <button className="primary-btn" onClick={handleAdd}>
          Add(17, 25)
        </button>
        <button className="primary-btn" onClick={handleAsync}>
          GetGreetingAsync (Task&lt;T&gt;)
        </button>
        <button className="primary-btn" onClick={handleGetPeople}>
          GetPeople()
        </button>
        <button className="primary-btn" onClick={handleGetEmployee}>
          GetEmployee (inheritance)
        </button>
      </div>

      <h2 className="section-title">Feature Tests</h2>
      <div className="actions">
        <button className="test-btn" onClick={handleEchoBytes}>
          byte[] round-trip
        </button>
        <button className="test-btn" onClick={handleValueTask}>
          ValueTask&lt;T&gt;
        </button>
        <button className="test-btn" onClick={handleTimeout}>
          Timeout (3s on 60s call)
        </button>
        <button className="test-btn" onClick={handleCancelStart}>
          Start Cancellable Call
        </button>
        <button className="test-btn cancel-btn" onClick={handleCancelAbort}>
          Cancel
        </button>
        <button className="test-btn" onClick={handleProgress}>
          Events (progress)
        </button>
      </div>

      <h2 className="section-title">Native dialogs (Dialog service)</h2>
      <p className="description" style={{ marginTop: 0 }}>
        Message, Ask, Confirm, Open file/folder, Save.
      </p>
      <div className="actions">
        <button className="primary-btn" onClick={handleMessage}>
          Message (Ok/Cancel)
        </button>
        <button className="primary-btn" onClick={handleAsk}>
          Ask (Yes/No)
        </button>
        <button className="primary-btn" onClick={handleConfirm}>
          Confirm (Ok/Cancel)
        </button>
        <button className="primary-btn" onClick={handleOpenFile}>
          Open file(s)
        </button>
        <button className="primary-btn" onClick={handleOpenFolder}>
          Open folder
        </button>
        <button className="primary-btn" onClick={handleSave}>
          Save file
        </button>
      </div>
      {dialogResult && (
        <div className="result-item" style={{ marginTop: 8 }}>
          <strong>Dialog:</strong> {dialogResult}
        </div>
      )}

      <h2 className="section-title">Tray (TrayService)</h2>
      <p className="description" style={{ marginTop: 0 }}>
        Tray icons are created from C# before Run; you can change the icon, tooltip, menu, and more from JS. Use the bridge to get tray IDs, set icon (base64), tooltip, visibility, menu options, mutate menu items, and subscribe to tray events.
      </p>
      {trayError && <div className="error">{trayError}</div>}
      <div className="actions">
        <button className="test-btn" onClick={handleTrayRefreshIds}>
          Refresh tray IDs
        </button>
        {firstTrayId != null && (
          <>
            <div style={{ display: "flex", alignItems: "center", gap: "0.5rem", flexWrap: "wrap" }}>
              <input
                type="text"
                placeholder="Tooltip text"
                value={trayTooltipInput}
                onChange={(e) => setTrayTooltipInput(e.target.value)}
                style={{ padding: "0.4rem 0.6rem", minWidth: 160 }}
              />
              <button className="test-btn" onClick={handleSetTrayTooltip}>
                Set tooltip
              </button>
            </div>
            <input
              ref={trayIconInputRef}
              type="file"
              accept=".png,.ico,.jpg,.jpeg,.gif,.bmp"
              style={{ display: "none" }}
              onChange={handleSetTrayIconFromFile}
            />
            <button
              className="test-btn"
              type="button"
              onClick={() => trayIconInputRef.current?.click()}
            >
              Set icon from file
            </button>
            <label style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
              <input type="checkbox" onChange={(e) => handleSetTrayVisible(e.target.checked)} />
              Set visible
            </label>
            <label style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
              <input type="checkbox" onChange={(e) => handleSetMenuOnLeftClick(e.target.checked)} />
              Menu on left click
            </label>
            <button className="test-btn" onClick={handleAppendTrayItemFromJs}>
              Append item from JS
            </button>
            <button className="test-btn" onClick={handleSetShowWindowLabel}>
              Set &quot;Show Window&quot; label
            </button>
            <button className="test-btn" onClick={handleRemoveJsDemoItem}>
              Remove &quot;Added from JS&quot; item
            </button>
          </>
        )}
      </div>
      {(trayIds.length > 0 || lastTrayMenuItemClicked || lastTrayIconEvent) && (
        <div className="result-item" style={{ marginTop: 8, width: "100%", maxWidth: 480 }}>
          <strong>Tray:</strong>{" "}
          {trayIds.length > 0 ? `IDs: [${trayIds.join(", ")}]` : "No trays"}
          {lastTrayMenuItemClicked && (
            <div style={{ marginTop: 4 }}>
              Last menu click: trayId={lastTrayMenuItemClicked.trayId}, itemId=&quot;{lastTrayMenuItemClicked.itemId}&quot;
            </div>
          )}
          {lastTrayIconEvent && (
            <div style={{ marginTop: 4 }}>
              Last icon event: {lastTrayIconEvent.type}
              {lastTrayIconEvent.position && ` at (${lastTrayIconEvent.position.x}, ${lastTrayIconEvent.position.y})`}
              {lastTrayIconEvent.button != null && ` button=${lastTrayIconEvent.button}`}
            </div>
          )}
        </div>
      )}

      <div className="results">
        {greeting && (
          <div className="result-item">
            <strong>Greet:</strong> {greeting}
          </div>
        )}
        {person && (
          <div className="result-item">
            <strong>GetPerson:</strong> {person.name}, age {person.age}
            {person.email ? `, email: ${person.email}` : ""}
            {person.display_name ? `, display_name: "${person.display_name}"` : ""}
          </div>
        )}
        {sum !== null && (
          <div className="result-item">
            <strong>Add:</strong> 17 + 25 = {sum}
          </div>
        )}
        {asyncMsg && (
          <div className="result-item">
            <strong>GetGreetingAsync:</strong> {asyncMsg}
          </div>
        )}
        {people.length > 0 && (
          <div className="result-item">
            <strong>GetPeople:</strong>
            <ul>
              {people.map((p, i) => (
                <li key={i}>
                  {p.name}, age {p.age}
                  {p.email ? ` (${p.email})` : ""}
                </li>
              ))}
            </ul>
          </div>
        )}
        {employee && (
          <div className="result-item">
            <strong>GetEmployee:</strong> {employee.name}, age {employee.age}
            , dept: {employee.department}, title: {employee.title}
            {employee.display_name ? `, display_name: "${employee.display_name}"` : ""}
            <br />
            <small style={{ color: "#888" }}>
              (inherits name, age, email, display_name from Person)
            </small>
          </div>
        )}
        {bytesResult && (
          <div className="result-item">
            <strong>EchoBytes:</strong> {bytesResult}
          </div>
        )}
        {valueTaskResult && (
          <div className="result-item">
            <strong>ValueTask:</strong> {valueTaskResult}
          </div>
        )}
        {timeoutResult && (
          <div className="result-item">
            <strong>Timeout:</strong> {timeoutResult}
          </div>
        )}
        {cancelResult && (
          <div className="result-item">
            <strong>Cancel:</strong> {cancelResult}
          </div>
        )}
        {(progressMsg || progressResult) && (
          <div className="result-item">
            <strong>Events:</strong>{" "}
            {progressResult ? (
              progressResult
            ) : (
              <>
                {progressMsg}
                <div
                  style={{
                    marginTop: 4,
                    height: 8,
                    background: "rgba(100,108,255,0.15)",
                    borderRadius: 4,
                    overflow: "hidden",
                  }}
                >
                  <div
                    style={{
                      width: `${progressPercent}%`,
                      height: "100%",
                      background: "#646cff",
                      borderRadius: 4,
                      transition: "width 0.3s ease",
                    }}
                  />
                </div>
                <small style={{ color: "#888" }}>{progressPercent}%</small>
              </>
            )}
          </div>
        )}
      </div>
    </div>
  );
}

export default App;
