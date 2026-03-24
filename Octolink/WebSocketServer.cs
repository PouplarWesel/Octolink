using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Octolink
{
    /// <summary>
    /// WebSocket server for real-time controller input with minimal latency.
    /// </summary>
    public class WebSocketServer
    {
        private readonly int _port;
        private readonly string _localIp;
        private readonly VirtualControllerManager _controllerManager;
        private readonly Action<ControllerUpdateEvent> _onUpdate;
        private HttpListener? _listener;
        private readonly ConcurrentDictionary<string, ConnectedClient> _clients = new();
        private CancellationTokenSource? _cts;
        private bool _running;
        private ControllerLayout? _currentLayout;
        private bool _loopbackOnly;
        private string? _publicWebSocketUrl;
        private Task? _watchdogTask;

        public WebSocketServer(int port, string localIp, VirtualControllerManager controllerManager, Action<ControllerUpdateEvent> onUpdate)
        {
            _port = port;
            _localIp = localIp;
            _controllerManager = controllerManager;
            _onUpdate = onUpdate;
        }

        public bool IsLoopbackOnly => _loopbackOnly;

        public void SetPublicWebSocketUrl(string? websocketUrl)
        {
            _publicWebSocketUrl = websocketUrl;
        }

        public Task StartAsync()
        {
            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            _loopbackOnly = false;

            try
            {
                _listener.Prefixes.Add($"http://+:{_port}/");
                _listener.Start();
            }
            catch (HttpListenerException)
            {
                _listener = new HttpListener();
                try
                {
                    _listener.Prefixes.Add($"http://{_localIp}:{_port}/");
                    _listener.Prefixes.Add($"http://localhost:{_port}/");
                    _listener.Start();
                }
                catch (HttpListenerException)
                {
                    _listener = new HttpListener();
                    _listener.Prefixes.Add($"http://localhost:{_port}/");
                    _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
                    _listener.Start();
                    _loopbackOnly = true;
                }
            }

            _running = true;
            _ = Task.Run(AcceptClientsAsync);
            _watchdogTask = Task.Run(MonitorClientsAsync);
            return Task.CompletedTask;
        }

        private async Task MonitorClientsAsync()
        {
            while (_running)
            {
                try
                {
                    await Task.Delay(1000, _cts?.Token ?? CancellationToken.None);
                }
                catch
                {
                    break;
                }

                var staleCutoff = DateTime.UtcNow.AddSeconds(-15);
                foreach (var client in _clients.Values.ToArray())
                {
                    if (client.WebSocket?.State != WebSocketState.Open) continue;

                    if (client.LastSeenUtc < staleCutoff)
                    {
                        if (client.ControllerIndex >= 0)
                        {
                            ForceReleaseInputs(client.ControllerIndex);
                            _controllerManager.RemoveController(client.ControllerIndex);
                            _onUpdate(new ControllerUpdateEvent
                            {
                                Type = UpdateEventType.Disconnected,
                                ControllerIndex = client.ControllerIndex
                            });
                        }

                        try
                        {
                            await client.WebSocket.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, "Connection timed out", CancellationToken.None);
                        }
                        catch { }

                        _clients.TryRemove(client.ClientId, out _);
                    }
                }
            }
        }

        private async Task AcceptClientsAsync()
        {
            while (_running && _listener != null)
            {
                try
                {
                    var context = await _listener.GetContextAsync();

                    if (context.Request.IsWebSocketRequest)
                    {
                        _ = Task.Run(() => HandleWebSocketAsync(context));
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                        context.Response.Close();
                    }
                }
                catch (Exception ex) when (ex is HttpListenerException || ex is ObjectDisposedException)
                {
                    break;
                }
            }
        }

        private async Task HandleWebSocketAsync(HttpListenerContext context)
        {
            WebSocket? webSocket = null;
            string clientId = Guid.NewGuid().ToString();
            int controllerIndex = -1;

            try
            {
                var wsContext = await context.AcceptWebSocketAsync(null);
                webSocket = wsContext.WebSocket;

                _clients[clientId] = new ConnectedClient
                {
                    ClientId = clientId,
                    ControllerIndex = -1,
                    PlayerName = "",
                    WebSocket = webSocket,
                    LastSeenUtc = DateTime.UtcNow,
                    LastInputSeq = 0
                };

                await SendMessageAsync(webSocket, new
                {
                    type = "serverInfo",
                    websocketUrl = _publicWebSocketUrl,
                    loopbackOnly = _loopbackOnly
                });

                // iOS Safari can be slow to finalize the first message after page load.
                // Give it a brief window before reading so the initial connect isn't dropped.
                await Task.Delay(50);

                var buffer = new byte[4096];

                while (webSocket.State == WebSocketState.Open && _running)
                {
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts?.Token ?? CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        controllerIndex = await ProcessMessageAsync(clientId, controllerIndex, message, webSocket);
                        if (_clients.TryGetValue(clientId, out var client))
                        {
                            client.LastSeenUtc = DateTime.UtcNow;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WebSocket error: {ex.Message}");
            }
            finally
            {
                if (controllerIndex >= 0 && _clients.TryGetValue(clientId, out var client) && client.ControllerIndex == controllerIndex)
                {
                    ForceReleaseInputs(controllerIndex);
                    _controllerManager.RemoveController(controllerIndex);
                    _onUpdate(new ControllerUpdateEvent
                    {
                        Type = UpdateEventType.Disconnected,
                        ControllerIndex = controllerIndex
                    });
                }

                _clients.TryRemove(clientId, out _);
                webSocket?.Dispose();
            }
        }

        private async Task<int> ProcessMessageAsync(string clientId, int currentIndex, string message, WebSocket webSocket)
        {
            try
            {
                using var json = JsonDocument.Parse(message);
                var root = json.RootElement;
                var type = root.TryGetProperty("type", out var typeElem) ? typeElem.GetString() : null;

                return type switch
                {
                    "connect" => await HandleConnectAsync(clientId, root, webSocket),
                    "reconnect" => await HandleReconnectAsync(clientId, root, currentIndex, webSocket),
                    "input" => HandleInput(clientId, currentIndex, root),
                    "heartbeat" => currentIndex,
                    _ => currentIndex
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing message: {ex.Message}");
                return currentIndex;
            }
        }

        private async Task<int> HandleConnectAsync(string clientId, JsonElement root, WebSocket webSocket)
        {
            string playerName = root.TryGetProperty("name", out var nameElem) ? nameElem.GetString() ?? "Player" : "Player";
            playerName = string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName.Trim();

            int slot = _controllerManager.GetNextAvailableSlot();
            if (slot < 0)
            {
                await SendMessageAsync(webSocket, new { type = "error", message = "No slots available" });
                return -1;
            }

            if (!_controllerManager.CreateController(slot))
            {
                await SendMessageAsync(webSocket, new { type = "error", message = "Failed to create controller" });
                return -1;
            }

            if (_clients.TryGetValue(clientId, out var client))
            {
                client.ControllerIndex = slot;
                client.PlayerName = playerName;
                client.LastSeenUtc = DateTime.UtcNow;
                client.LastInputSeq = 0;
            }

            await SendMessageAsync(webSocket, new { type = "assigned", slot = slot + 1, name = playerName });

            if (_currentLayout != null)
            {
                await SendLayoutToClientAsync(webSocket, _currentLayout);
            }

            _onUpdate(new ControllerUpdateEvent
            {
                Type = UpdateEventType.Connected,
                ControllerIndex = slot,
                PlayerName = playerName
            });

            return slot;
        }

        private async Task<int> HandleReconnectAsync(string clientId, JsonElement root, int currentIndex, WebSocket webSocket)
        {
            if (!root.TryGetProperty("slot", out var slotElem))
            {
                await SendMessageAsync(webSocket, new { type = "error", message = "Missing slot number" });
                return currentIndex;
            }

            int requestedSlot = slotElem.GetInt32() - 1;
            string playerName = root.TryGetProperty("name", out var nameElem) ? nameElem.GetString() ?? "Player" : "Player";
            playerName = string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName.Trim();

            if (requestedSlot < 0 || requestedSlot >= VirtualControllerManager.MaxControllers)
            {
                await SendMessageAsync(webSocket, new { type = "error", message = "Invalid slot number" });
                return currentIndex;
            }

            if (!_controllerManager.IsSlotAvailable(requestedSlot))
            {
                await SendMessageAsync(webSocket, new { type = "error", message = "Slot is already taken" });
                return currentIndex;
            }

            if (currentIndex >= 0)
            {
                ForceReleaseInputs(currentIndex);
                _controllerManager.RemoveController(currentIndex);
                _onUpdate(new ControllerUpdateEvent
                {
                    Type = UpdateEventType.Disconnected,
                    ControllerIndex = currentIndex
                });
            }

            if (!_controllerManager.CreateController(requestedSlot))
            {
                await SendMessageAsync(webSocket, new { type = "error", message = "Failed to create controller" });
                return -1;
            }

            if (_clients.TryGetValue(clientId, out var client))
            {
                client.ControllerIndex = requestedSlot;
                client.PlayerName = playerName;
                client.LastSeenUtc = DateTime.UtcNow;
                client.LastInputSeq = 0;
            }

            await SendMessageAsync(webSocket, new { type = "assigned", slot = requestedSlot + 1, name = playerName });

            if (_currentLayout != null)
            {
                await SendLayoutToClientAsync(webSocket, _currentLayout);
            }

            _onUpdate(new ControllerUpdateEvent
            {
                Type = UpdateEventType.Connected,
                ControllerIndex = requestedSlot,
                PlayerName = playerName
            });

            return requestedSlot;
        }

        private int HandleInput(string clientId, int controllerIndex, JsonElement root)
        {
            if (controllerIndex < 0) return controllerIndex;

            if (_clients.TryGetValue(clientId, out var client))
            {
                if (root.TryGetProperty("seq", out var seqElem) && seqElem.TryGetInt64(out var seq))
                {
                    if (seq <= client.LastInputSeq)
                    {
                        client.LastSeenUtc = DateTime.UtcNow;
                        return controllerIndex;
                    }

                    client.LastInputSeq = seq;
                }

                client.LastSeenUtc = DateTime.UtcNow;
            }

            var state = new ControllerState
            {
                ButtonA = GetBool(root, "a"),
                ButtonB = GetBool(root, "b"),
                ButtonX = GetBool(root, "x"),
                ButtonY = GetBool(root, "y"),
                LeftBumper = GetBool(root, "lb"),
                RightBumper = GetBool(root, "rb"),
                Back = GetBool(root, "back"),
                Start = GetBool(root, "start"),
                Guide = GetBool(root, "guide"),
                LeftStickClick = GetBool(root, "ls"),
                RightStickClick = GetBool(root, "rs"),
                DPadUp = GetBool(root, "up"),
                DPadDown = GetBool(root, "down"),
                DPadLeft = GetBool(root, "left"),
                DPadRight = GetBool(root, "right"),
                LeftTrigger = GetFloat(root, "lt"),
                RightTrigger = GetFloat(root, "rt"),
                LeftStickX = GetFloat(root, "lx"),
                LeftStickY = GetFloat(root, "ly"),
                RightStickX = GetFloat(root, "rx"),
                RightStickY = GetFloat(root, "ry")
            };

            _controllerManager.UpdateController(controllerIndex, state);

            _onUpdate(new ControllerUpdateEvent
            {
                Type = UpdateEventType.Input,
                ControllerIndex = controllerIndex,
                State = state
            });

            return controllerIndex;
        }

        private static bool GetBool(JsonElement root, string prop)
        {
            return root.TryGetProperty(prop, out var elem) && elem.ValueKind == JsonValueKind.True;
        }

        private static float GetFloat(JsonElement root, string prop)
        {
            if (root.TryGetProperty(prop, out var elem))
            {
                return elem.ValueKind switch
                {
                    JsonValueKind.Number => (float)elem.GetDouble(),
                    _ => 0
                };
            }

            return 0;
        }

        private static async Task SendMessageAsync(WebSocket webSocket, object data)
        {
            if (webSocket.State != WebSocketState.Open) return;

            try
            {
                var json = JsonSerializer.Serialize(data);
                var bytes = Encoding.UTF8.GetBytes(json);
                await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch { }
        }

        public void Stop()
        {
            _running = false;
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            try { _listener?.Close(); } catch { }
            _listener = null;
            _clients.Clear();
        }

        public void BroadcastLayout(ControllerLayout layout)
        {
            _currentLayout = layout;

            var json = layout.ToJson();
            var bytes = Encoding.UTF8.GetBytes(json);

            foreach (var client in _clients.Values)
            {
                if (client.WebSocket?.State == WebSocketState.Open)
                {
                    try
                    {
                        _ = client.WebSocket.SendAsync(
                            new ArraySegment<byte>(bytes),
                            WebSocketMessageType.Text,
                            true,
                            CancellationToken.None);
                    }
                    catch { }
                }
            }
        }

        private async Task SendLayoutToClientAsync(WebSocket webSocket, ControllerLayout layout)
        {
            try
            {
                var json = layout.ToJson();
                var bytes = Encoding.UTF8.GetBytes(json);
                await webSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None);
            }
            catch { }
        }

        public void SetCurrentLayout(ControllerLayout layout)
        {
            _currentLayout = layout;
        }

        public void ForceReleaseInputs(int controllerIndex)
        {
            if (controllerIndex < 0 || controllerIndex >= VirtualControllerManager.MaxControllers) return;

            _controllerManager.UpdateController(controllerIndex, new ControllerState());
        }

        public void KickController(int controllerIndex)
        {
            var clientToKick = _clients.Values.FirstOrDefault(c => c.ControllerIndex == controllerIndex);
            if (clientToKick?.WebSocket != null)
            {
                try
                {
                    ForceReleaseInputs(controllerIndex);
                    var kickMessage = JsonSerializer.Serialize(new { type = "kicked", message = "You have been disconnected by the server" });
                    var bytes = Encoding.UTF8.GetBytes(kickMessage);

                    clientToKick.WebSocket.SendAsync(
                        new ArraySegment<byte>(bytes),
                        WebSocketMessageType.Text,
                        true,
                        CancellationToken.None).Wait(500);

                    clientToKick.WebSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Kicked by server",
                        CancellationToken.None).Wait(500);
                }
                catch { }
            }
        }
    }

        public class ConnectedClient
        {
        public required string ClientId { get; set; }
        public int ControllerIndex { get; set; }
        public required string PlayerName { get; set; }
        public WebSocket? WebSocket { get; set; }
        public DateTime LastSeenUtc { get; set; }
        public long LastInputSeq { get; set; }
        }

    public class ControllerUpdateEvent
    {
        public UpdateEventType Type { get; set; }
        public int ControllerIndex { get; set; }
        public string? PlayerName { get; set; }
        public ControllerState? State { get; set; }
    }

    public enum UpdateEventType
    {
        Connected,
        Disconnected,
        Input
    }
}
