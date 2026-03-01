using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Wry.NET;

namespace Wry.NET.Bridge;

/// <summary>
/// Core bridge that connects wry's IPC message channel to
/// reflection-based method dispatch on registered service instances.
/// </summary>
public class WryBridge
{
    private readonly Dictionary<string, RegisteredService> _services = new();
    private readonly ILogger<WryBridge> _logger;
    private readonly List<WryWindow> _attachedWindows = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Tracks in-flight calls so they can be cancelled from JS.
    /// Key = callId, Value = CancellationTokenSource for that call.
    /// </summary>
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _inFlightCalls = new();

    /// <summary>
    /// Creates a new WryBridge instance.
    /// </summary>
    /// <param name="logger">Optional logger. Pass <c>null</c> to disable logging.</param>
    public WryBridge(ILogger<WryBridge>? logger = null)
    {
        _logger = logger ?? NullLogger<WryBridge>.Instance;
    }

    /// <summary>
    /// Register a service instance. All public instance methods declared on T
    /// become callable from JS as "ServiceName.MethodName".
    /// </summary>
    public WryBridge RegisterService<T>(T instance) where T : class
    {
        var type = typeof(T);
        var attr = type.GetCustomAttribute<BridgeServiceAttribute>();
        var serviceName = attr?.Name ?? type.Name;

        var methods = new Dictionary<string, MethodInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (method.IsSpecialName) continue; // skip property accessors, event accessors
            if (method.GetCustomAttribute<BridgeIgnoreAttribute>() != null) continue;
            methods[method.Name] = method;
        }

        _services[serviceName] = new RegisteredService(instance, methods);
        _logger.LogInformation("Registered service '{ServiceName}' with {MethodCount} method(s)", serviceName, methods.Count);
        return this;
    }

    /// <summary>
    /// Attach this bridge to a WryWindow. Registers the IPC message handler
    /// and an init script for C#→JS messaging. Can be called for multiple windows;
    /// responses are routed back to the window that sent the request, and
    /// <see cref="Emit"/> broadcasts to all attached windows.
    /// Must be called before <see cref="WryApp.Run"/> (or before the window is created for dynamic windows).
    /// </summary>
    public void Attach(WryWindow window)
    {
        lock (_attachedWindows)
        {
            if (_attachedWindows.Contains(window))
                return;
            _attachedWindows.Add(window);
        }

        // Register the init script that sets up the C#→JS message receiver.
        window.AddInitScript("""
            (function() {
                const listeners = [];
                window.__bridge_receive = function(raw) {
                    for (const cb of listeners) cb(raw);
                };
                window.external = window.external || {};
                window.external.receiveMessage = function(cb) {
                    listeners.push(cb);
                };
                window.external.sendMessage = function(msg) {
                    window.ipc.postMessage(msg);
                };
            })();
            """);

        window.IpcMessageReceived += (sender, e) => HandleMessage(sender, e.Message);
        window.WindowDestroyed += OnAttachedWindowDestroyed;
    }

    private void OnAttachedWindowDestroyed(object? sender, EventArgs _)
    {
        if (sender is WryWindow w)
        {
            w.WindowDestroyed -= OnAttachedWindowDestroyed;
            lock (_attachedWindows)
            {
                _attachedWindows.Remove(w);
            }
        }
    }

    /// <summary>
    /// Message handler. Handles both call requests and cancel requests.
    /// Responses are sent back to the window that sent the message.
    /// </summary>
    public async void HandleMessage(object? sender, string rawMessage)
    {
        var sourceWindow = sender as WryWindow;

        // Try to detect cancel messages first (they have a "cancel" field)
        if (TryHandleCancelMessage(rawMessage))
            return;

        BridgeRequest? request = null;
        try
        {
            request = JsonSerializer.Deserialize<BridgeRequest>(rawMessage, JsonOptions);
            if (request == null || string.IsNullOrEmpty(request.CallId))
                return;

            // Parse "ServiceName.MethodName"
            var dotIndex = request.Method.IndexOf('.');
            if (dotIndex < 0)
                throw new ArgumentException($"Invalid method format: '{request.Method}'. Expected 'ServiceName.MethodName'.");

            var serviceName = request.Method[..dotIndex];
            var methodName = request.Method[(dotIndex + 1)..];

            if (!_services.TryGetValue(serviceName, out var service))
                throw new InvalidOperationException($"Service '{serviceName}' not found. Registered services: {string.Join(", ", _services.Keys)}");

            if (!service.Methods.TryGetValue(methodName, out var method))
                throw new InvalidOperationException($"Method '{methodName}' not found on service '{serviceName}'.");

            // Track in-flight call with a CancellationTokenSource
            var cts = new CancellationTokenSource();
            _inFlightCalls.TryAdd(request.CallId, cts);

            try
            {
                // Build arguments from JSON, injecting CallContext and CancellationToken when declared
                var parameters = method.GetParameters();
                var args = new object?[parameters.Length];
                int jsonArgIndex = 0;
                var callContext = new CallContext(sourceWindow);

                for (int i = 0; i < parameters.Length; i++)
                {
                    var paramType = parameters[i].ParameterType;
                    // Inject call context when parameter is CallContext (Wails-style "inject when declared")
                    if (paramType == typeof(CallContext))
                    {
                        args[i] = callContext;
                    }
                    // Auto-inject CancellationToken — not supplied from JS
                    else if (paramType == typeof(CancellationToken))
                    {
                        args[i] = cts.Token;
                    }
                    else if (request.Args != null && jsonArgIndex < request.Args.Length)
                    {
                        args[i] = request.Args[jsonArgIndex].Deserialize(parameters[i].ParameterType, JsonOptions);
                        jsonArgIndex++;
                    }
                    else if (parameters[i].HasDefaultValue)
                    {
                        args[i] = parameters[i].DefaultValue;
                    }
                    else
                    {
                        throw new ArgumentException(
                            $"Missing required argument '{parameters[i].Name}' (index {i}) for method '{request.Method}'.");
                    }
                }

                // Invoke the method and await the result
                var rawResult = method.Invoke(service.Instance, args);
                var result = await UnwrapAsyncResult(rawResult);

                SendResponse(sourceWindow, new BridgeResponse
                {
                    CallId = request.CallId,
                    Result = result
                });
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                // Call was cancelled by the JS side — send a specific error
                _logger.LogDebug("Call '{Method}' ({CallId}) was cancelled", request.Method, request.CallId);
                SendResponse(sourceWindow, new BridgeResponse
                {
                    CallId = request.CallId,
                    Error = new BridgeError
                    {
                        Message = $"Call '{request.Method}' was cancelled.",
                        Type = "OperationCanceledException"
                    }
                });
            }
            finally
            {
                _inFlightCalls.TryRemove(request.CallId, out _);
                cts.Dispose();
            }
        }
        catch (Exception ex)
        {
            // Unwrap TargetInvocationException to get the real exception
            var error = (ex is TargetInvocationException { InnerException: { } inner })
                ? inner
                : ex;

            // If the real exception is cancellation, handle it gracefully
            if (error is OperationCanceledException)
            {
                _logger.LogDebug("Call '{Method}' ({CallId}) was cancelled", request?.Method, request?.CallId);
                SendResponse(sourceWindow, new BridgeResponse
                {
                    CallId = request?.CallId ?? "",
                    Error = new BridgeError
                    {
                        Message = $"Call '{request?.Method}' was cancelled.",
                        Type = "OperationCanceledException"
                    }
                });
                return;
            }

            _logger.LogError(error, "Error handling '{Method}'", request?.Method);

            SendResponse(sourceWindow, new BridgeResponse
            {
                CallId = request?.CallId ?? "",
                Error = new BridgeError
                {
                    Message = error.Message,
                    Type = error.GetType().Name
                }
            });
        }
    }

    /// <summary>
    /// Try to parse the incoming message as a cancel request.
    /// Cancel messages have the form: { "cancel": "callId" }
    /// </summary>
    private bool TryHandleCancelMessage(string rawMessage)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawMessage);
            if (doc.RootElement.TryGetProperty("cancel", out var cancelProp))
            {
                var callId = cancelProp.GetString();
                if (!string.IsNullOrEmpty(callId) && _inFlightCalls.TryGetValue(callId, out var cts))
                {
                    _logger.LogDebug("Received cancel request for callId '{CallId}'", callId);
                    try
                    {
                        cts.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        // CTS was already disposed — call already completed
                    }
                }
                return true; // It was a cancel message (even if the callId wasn't found)
            }
        }
        catch (JsonException)
        {
            // Not valid JSON — fall through to normal handling
        }
        return false;
    }

    /// <summary>
    /// Awaits Task, Task&lt;T&gt;, ValueTask, and ValueTask&lt;T&gt; return values
    /// and extracts the result. Synchronous return values pass through unchanged.
    /// </summary>
    private static async Task<object?> UnwrapAsyncResult(object? rawResult)
    {
        if (rawResult == null)
            return null;

        var resultType = rawResult.GetType();

        // Task (no result)
        if (rawResult is Task { } task)
        {
            await task;
            // Check if it's Task<T> (has a result)
            if (resultType.IsGenericType)
                return resultType.GetProperty("Result")!.GetValue(task);
            return null;
        }

        // ValueTask (no result)
        if (rawResult is ValueTask valueTask)
        {
            await valueTask;
            return null;
        }

        // ValueTask<T> -- must be handled via reflection since ValueTask<T> is a struct
        // and doesn't share a base type. Check if it implements GetAwaiter().
        if (resultType.IsGenericType &&
            resultType.FullName?.StartsWith("System.Threading.Tasks.ValueTask`1") == true)
        {
            // Convert ValueTask<T> to Task<T> via AsTask() then await
            var asTaskMethod = resultType.GetMethod("AsTask")!;
            var taskObj = (Task)asTaskMethod.Invoke(rawResult, null)!;
            await taskObj;
            return taskObj.GetType().GetProperty("Result")!.GetValue(taskObj);
        }

        // Synchronous result -- return as-is
        return rawResult;
    }

    /// <summary>
    /// Emit an event to the JS frontend in all attached windows. Fire-and-forget.
    /// Can be called from any thread — automatically dispatches to the UI thread.
    /// </summary>
    /// <param name="eventName">The event name (matches the JS <c>events.on(name, cb)</c> subscription).</param>
    /// <param name="data">Optional payload. Will be serialized to JSON with camelCase naming.</param>
    public void Emit(string eventName, object? data = null)
    {
        var msg = new BridgeEventMessage { Event = eventName, Data = data };
        var json = JsonSerializer.Serialize(msg, JsonOptions);
        lock (_attachedWindows)
        {
            foreach (var w in _attachedWindows)
                SendToJs(w, json);
        }
    }

    /// <summary>
    /// Sends a JSON response to the window that sent the request.
    /// </summary>
    private void SendResponse(WryWindow? targetWindow, BridgeResponse response)
    {
        SendToJs(targetWindow, JsonSerializer.Serialize(response, JsonOptions));
    }

    /// <summary>
    /// Sends a raw JSON string to a specific window's JS via EvalJs.
    /// </summary>
    private void SendToJs(WryWindow? window, string json)
    {
        if (window is not { } win || !win.IsLive) return;

        var escaped = json.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n").Replace("\r", "\\r");

        win.Dispatch(w =>
        {
            try
            {
                w.EvalJs($"window.__bridge_receive('{escaped}')");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send message via EvalJs");
            }
        });
    }

    private sealed record RegisteredService(object Instance, Dictionary<string, MethodInfo> Methods);
}

#region Wire protocol types

internal class BridgeRequest
{
    public string CallId { get; set; } = "";
    public string Method { get; set; } = "";
    public JsonElement[]? Args { get; set; }
}

internal class BridgeResponse
{
    public string CallId { get; set; } = "";
    public object? Result { get; set; }
    public BridgeError? Error { get; set; }
}

internal class BridgeError
{
    public string Message { get; set; } = "";
    public string Type { get; set; } = "";
}

internal class BridgeEventMessage
{
    public string Event { get; set; } = "";
    public object? Data { get; set; }
}

#endregion
