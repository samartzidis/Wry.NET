import { useCallback } from "react";
import { BackendService } from "./bindings";
import "./index.css";

/**
 * Minimal content for the child window (one SPA, route ?window=child).
 * Button press notifies the parent window via bridge emit.
 */
function ChildWindow() {
  const handlePress = useCallback(() => {
    void BackendService.NotifyParent("Button pressed!");
  }, []);

  return (
    <div className="container" style={{ padding: "2rem", textAlign: "center" }}>
      <h1>Child Window</h1>
      <p style={{ marginBottom: "1.5rem" }}>
        This window uses the same SPA with a different route.
      </p>
      <button className="primary-btn" onClick={handlePress}>
        Press me
      </button>
    </div>
  );
}

export default ChildWindow;
