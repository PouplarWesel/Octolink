using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;

namespace VirtualControllerServer
{
    /// <summary>
    /// WebSocket server for real-time controller input with minimal latency
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

        public WebSocketServer(int port, string localIp, VirtualControllerManager controllerManager, Action<ControllerUpdateEvent> onUpdate)
        {
            _port = port;
            _localIp = localIp;
            _controllerManager = controllerManager;
            _onUpdate = onUpdate;
        }

        public async Task StartAsync()
        {
            _cts = new CancellationTokenSource();
            _listener = new HttpListener();
            
            try
            {
                // Try binding to all interfaces (works after running Setup.bat as admin once)
                _listener.Prefixes.Add($"http://+:{_port}/");
                _listener.Start();
            }
            catch (HttpListenerException)
            {
                // Fallback: try specific IP
                _listener = new HttpListener();
                try
                {
                    _listener.Prefixes.Add($"http://{_localIp}:{_port}/");
                    _listener.Prefixes.Add($"http://localhost:{_port}/");
                    _listener.Start();
                }
                catch (HttpListenerException)
                {
                    // Last resort: localhost only
                    _listener = new HttpListener();
                    _listener.Prefixes.Add($"http://localhost:{_port}/");
                    _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
                    _listener.Start();
                }
            }

            _running = true;
            _ = Task.Run(AcceptClientsAsync);
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
            WebSocketContext? wsContext = null;
            WebSocket? webSocket = null;
            string clientId = Guid.NewGuid().ToString();
            int controllerIndex = -1;

            try
            {
                wsContext = await context.AcceptWebSocketAsync(null);
                webSocket = wsContext.WebSocket;

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
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"WebSocket error: {ex.Message}");
            }
            finally
            {
                // Clean up on disconnect
                if (controllerIndex >= 0)
                {
                    _controllerManager.RemoveController(controllerIndex);
                    _clients.TryRemove(clientId, out _);
                    _onUpdate(new ControllerUpdateEvent
                    {
                        Type = UpdateEventType.Disconnected,
                        ControllerIndex = controllerIndex
                    });
                }

                webSocket?.Dispose();
            }
        }

        private async Task<int> ProcessMessageAsync(string clientId, int currentIndex, string message, WebSocket webSocket)
        {
            try
            {
                var json = JsonDocument.Parse(message);
                var root = json.RootElement;
                var type = root.GetProperty("type").GetString();

                switch (type)
                {
                    case "connect":
                        return await HandleConnectAsync(clientId, root, webSocket);
                    
                    case "reconnect":
                        return await HandleReconnectAsync(clientId, root, currentIndex, webSocket);
                    
                    case "input":
                        HandleInput(currentIndex, root);
                        return currentIndex;
                    
                    default:
                        return currentIndex;
                }
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

            _clients[clientId] = new ConnectedClient { ClientId = clientId, ControllerIndex = slot, PlayerName = playerName, WebSocket = webSocket };

            await SendMessageAsync(webSocket, new { type = "assigned", slot = slot + 1, name = playerName });

            // Send current layout to newly connected client
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
            int requestedSlot = root.GetProperty("slot").GetInt32() - 1; // Convert to 0-based
            string playerName = root.TryGetProperty("name", out var nameElem) ? nameElem.GetString() ?? "Player" : "Player";

            if (requestedSlot < 0 || requestedSlot >= 8)
            {
                await SendMessageAsync(webSocket, new { type = "error", message = "Invalid slot number" });
                return currentIndex;
            }

            if (!_controllerManager.IsSlotAvailable(requestedSlot))
            {
                await SendMessageAsync(webSocket, new { type = "error", message = "Slot is already taken" });
                return currentIndex;
            }

            // Remove old controller if exists
            if (currentIndex >= 0)
            {
                _controllerManager.RemoveController(currentIndex);
                _onUpdate(new ControllerUpdateEvent
                {
                    Type = UpdateEventType.Disconnected,
                    ControllerIndex = currentIndex
                });
            }

            // Create new controller at requested slot
            if (!_controllerManager.CreateController(requestedSlot))
            {
                await SendMessageAsync(webSocket, new { type = "error", message = "Failed to create controller" });
                return -1;
            }

            _clients[clientId] = new ConnectedClient { ClientId = clientId, ControllerIndex = requestedSlot, PlayerName = playerName, WebSocket = webSocket };

            await SendMessageAsync(webSocket, new { type = "assigned", slot = requestedSlot + 1, name = playerName });

            // Send current layout to reconnected client
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

        private void HandleInput(int controllerIndex, JsonElement root)
        {
            if (controllerIndex < 0) return;

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
            var json = JsonSerializer.Serialize(data);
            var bytes = Encoding.UTF8.GetBytes(json);
            await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        public void Stop()
        {
            _running = false;
            try
            {
                _cts?.Cancel();
            }
            catch { }
            try
            {
                _listener?.Stop();
            }
            catch { }
            try
            {
                _listener?.Close();
            }
            catch { }
            _listener = null;
            _clients.Clear();
        }

        public void BroadcastLayout(ControllerLayout layout)
        {
            _currentLayout = layout; // Store for new connections
            
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

        public void KickController(int controllerIndex)
        {
            var clientToKick = _clients.Values.FirstOrDefault(c => c.ControllerIndex == controllerIndex);
            if (clientToKick?.WebSocket != null)
            {
                try
                {
                    // Send kick message to client
                    var kickMessage = JsonSerializer.Serialize(new { type = "kicked", message = "You have been disconnected by the server" });
                    var bytes = Encoding.UTF8.GetBytes(kickMessage);
                    
                    clientToKick.WebSocket.SendAsync(
                        new ArraySegment<byte>(bytes),
                        WebSocketMessageType.Text,
                        true,
                        CancellationToken.None).Wait(500);
                    
                    // Close the connection
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
