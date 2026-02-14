# Wry.Bridge

A typed RPC bridge with auto-generated TypeScript bindings for [Wry.NET](https://github.com/samartzidis/Wry.NET)-based webview desktop applications.

Mark your C# services with `[BridgeService]`, run `dotnet build`, and get fully typed TypeScript functions with IntelliSense — no manual binding code, no code generators to run separately.

For full documentation, source code, and samples, see the [GitHub repository](https://github.com/samartzidis/Wry.NET).

## Binding Capabilities

| Capability | Description |
|---|---|
| Typed RPC | C# service methods marked with `[BridgeService]` are auto-generated as fully typed TypeScript functions. Call .NET methods from the frontend with full IntelliSense and compile-time type checking. |
| Async/Await | `Task<T>` and `ValueTask<T>` return types are unwrapped to `Promise<T>` in TypeScript. Synchronous methods become regular async calls. |
| Cancellation | Long-running .NET calls can be cancelled from JavaScript. `CancellationToken` parameters are auto-injected on the C# side and stripped from the generated TypeScript signature. Timeouts (default 30s) also trigger cancellation. |
| Events | .NET services push data to JavaScript via `bridge.Emit()`. The generator produces typed subscription helpers (`on`, `once`, `off`) for each `[BridgeEvent]` class. |
| Model Generation | C# classes and structs used in service signatures are generated as TypeScript interfaces with accurate property types. |
| Inheritance | C# class inheritance hierarchies are preserved using TypeScript `extends`. Only declared properties are emitted per level (no duplication). |
| Enum Generation | C# enums are generated as TypeScript enums with matching member names and values. |
| Nullable Types | `Nullable<T>` maps to `T \| null`. C# nullable reference types (NRT) are detected and emitted as optional properties (`prop?: type`). |
| Collections & Dictionaries | `List<T>`, `T[]`, `IEnumerable<T>` map to `T[]`. `Dictionary<K,V>` maps to `Record<K, V>`. |
| Binary Data | `byte[]` parameters and return types map to `string` (base64-encoded). |
| JSON Attribute Support | `[JsonPropertyName("x")]` overrides the generated property name. `[JsonIgnore]` excludes properties from generated interfaces. |
| Method Exclusion | `[BridgeIgnore]` hides individual methods from both the runtime dispatcher and the generated TypeScript. |
| Custom Service Names | `[BridgeService(Name = "CustomName")]` overrides the default class-name-based service identifier. |
| Stale File Cleanup | The generator tracks which files it produces and automatically deletes stale bindings that no longer correspond to a service, model, or event. |
| Index/Barrel Exports | An `index.ts` barrel file is generated, re-exporting all services, models, and event helpers for convenient single-point imports. |
| Zero-Config Build | MSBuild `.targets` auto-imported via NuGet — `dotnet build` runs the generator and copies the TypeScript runtime with no manual setup. |
| Framework Agnostic | The TypeScript runtime has no framework dependencies. Works with React, Vue, Svelte, Angular, or any TypeScript-capable setup. |

## Attributes

| Attribute | Target | Description |
|---|---|---|
| `[BridgeService]` | Class | Exposes the class as a callable service from JS |
| `[BridgeIgnore]` | Method | Hides a method from the JS boundary |
| `[BridgeEvent("name")]` | Class | Declares a typed event payload pushed from .NET to JS |

Standard `System.Text.Json` attributes (`[JsonPropertyName]`, `[JsonIgnore]`) are also respected in generated models.

## Cancellation

```typescript
import { cancellableCall } from "./bridge/runtime";

const p = cancellableCall<string>("GreetService.SlowMethod", 30);
// Cancel after 2 seconds:
setTimeout(() => p.cancel(), 2000);
```

On the C# side, add a `CancellationToken` parameter — it's automatically injected and invisible to JS:

```csharp
public async Task<string> SlowMethod(int steps, CancellationToken ct)
{
    for (int i = 0; i < steps; i++)
    {
        ct.ThrowIfCancellationRequested();
        await Task.Delay(1000, ct);
    }
    return "Done";
}
```

## Events

Declare a typed event in C#:

```csharp
[BridgeEvent("progress")]
public class ProgressEvent
{
    public int Percent { get; set; }
    public string Message { get; set; } = "";
}
```

Emit from a service:

```csharp
bridge.Emit("progress", new ProgressEvent { Percent = 50, Message = "Halfway" });
```

Subscribe in TypeScript (auto-generated typed helpers):

```typescript
import { onProgress } from "./bindings";

const unsub = onProgress((data) => {
    console.log(`${data.percent}% — ${data.message}`);
});
```

## Requirements

- .NET 8+
- Wry.NET
- A TypeScript frontend (any framework)

## License

MIT
