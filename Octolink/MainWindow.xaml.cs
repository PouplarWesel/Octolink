using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using System.Text.Json;
using QRCoder;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net.Http;
using System.Diagnostics;

namespace Octolink
{
    public partial class MainWindow : Window
    {
        private WebSocketServer? _webSocketServer;
        private HttpServer? _httpServer;
        private VirtualControllerManager? _controllerManager;
        private readonly ControllerDisplay[] _controllerDisplays = new ControllerDisplay[VirtualControllerManager.MaxControllers];
        private readonly int _port = 5000;
        private readonly int _webSocketPort = 5001;
        private string _localIp = "";
        private string _websocketConnectUrl = "";
        private string _publicHttpUrl = "";
        private Process? _ngrokHttpProcess;
        private Process? _ngrokWsProcess;
        private ControllerLayout _currentLayout = new();
        private bool _isUpdatingPreset = false;
        private static readonly string SettingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Octolink",
            "settings.json");

        public MainWindow()
        {
            InitializeComponent();
            InitializeControllerDisplays();
            InitializeLayoutPresets();
            LoadSavedSettings();
            GetLocalIPAddress();
        }

        private void InitializeLayoutPresets()
        {
            var presets = ControllerLayout.GetPresets();
            foreach (var preset in presets)
            {
                PresetComboBox.Items.Add(preset.Name);
            }
            PresetComboBox.SelectedIndex = 0;
        }

        private void InitializeControllerDisplays()
        {
            ControllersPanel.Items.Clear();
            for (int i = 0; i < VirtualControllerManager.MaxControllers; i++)
            {
                var display = new ControllerDisplay(i + 1);
                display.KickRequested += OnKickRequested;
                _controllerDisplays[i] = display;
                ControllersPanel.Items.Add(display);
            }
        }

        private void OnKickRequested(object? sender, int controllerIndex)
        {
            if (_webSocketServer == null) return;
            
            var result = MessageBox.Show(
                $"Kick Controller {controllerIndex + 1}?", 
                "Confirm Kick", 
                MessageBoxButton.YesNo, 
                MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                _webSocketServer.KickController(controllerIndex);
            }
        }

        private void GetLocalIPAddress()
        {
            try
            {
                // Get the most likely local IP (prefer WiFi/Ethernet)
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus == OperationalStatus.Up &&
                        (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                         ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet))
                    {
                        foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
                        {
                            if (ip.Address.AddressFamily == AddressFamily.InterNetwork &&
                                !IPAddress.IsLoopback(ip.Address))
                            {
                                _localIp = ip.Address.ToString();
                                break;
                            }
                        }
                    }
                    if (!string.IsNullOrEmpty(_localIp)) break;
                }

                if (string.IsNullOrEmpty(_localIp))
                {
                    _localIp = "127.0.0.1";
                }

                _publicHttpUrl = $"http://{_localIp}:{_port}";
                _websocketConnectUrl = $"ws://{_localIp}:{_webSocketPort}/";

                IpAddressText.Text = $"IP: {_localIp}";
                PortText.Text = $"Port: {_port}";
            }
            catch
            {
                _localIp = "127.0.0.1";
            }
        }

        private void GenerateQRCode(string url)
        {
            try
            {
                using var qrGenerator = new QRCodeGenerator();
                using var qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
                using var qrCode = new QRCode(qrCodeData);
                using var bitmap = qrCode.GetGraphic(10, System.Drawing.Color.FromArgb(26, 26, 46), System.Drawing.Color.White, true);
                
                QrCodeImage.Source = BitmapToImageSource(bitmap);
                UrlText.Text = url;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating QR code: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static BitmapImage BitmapToImageSource(Bitmap bitmap)
        {
            using var memory = new MemoryStream();
            bitmap.Save(memory, ImageFormat.Png);
            memory.Position = 0;
            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.StreamSource = memory;
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.EndInit();
            bitmapImage.Freeze();
            return bitmapImage;
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StartButton.IsEnabled = false;

                // Initialize controller manager
                _controllerManager = new VirtualControllerManager();
                
                // Apply axis inversion settings
                ApplyAxisInversionSettings();
                
                // Start HTTP server for serving the web interface
                _httpServer = new HttpServer(_port, _localIp);
                await _httpServer.StartAsync();

                // Start WebSocket server for real-time communication
                _webSocketServer = new WebSocketServer(_webSocketPort, _localIp, _controllerManager, UpdateUI);
                await _webSocketServer.StartAsync();

                // Set initial layout so new connections get it
                _webSocketServer.SetCurrentLayout(_currentLayout);

                // Generate QR code
                await ResolvePublicEndpointsAsync();
                _httpServer?.SetPublicEndpoints(_publicHttpUrl, _websocketConnectUrl);
                _webSocketServer?.SetPublicWebSocketUrl(_websocketConnectUrl);

                string url = _publicHttpUrl;
                GenerateQRCode(url);

                if ((_httpServer?.IsLoopbackOnly ?? false) || (_webSocketServer?.IsLoopbackOnly ?? false))
                {
                    StatusText.Text = "Local-only binding";
                    LayoutStatusText.Text = "Run as Administrator or use ngrok tunnels.";
                }

                NgrokStatusText.Text = "If iPhone connects to page but not controller, keep the tab open and retry once after typing the name.";

                // Update status
                StatusIndicator.Fill = (SolidColorBrush)FindResource("SuccessBrush");
                StatusText.Text = "Running";
                StopButton.IsEnabled = true;
                LaunchNgrokButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start server: {ex.Message}\n\nMake sure ViGEmBus driver is installed.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StartButton.IsEnabled = true;
                StatusIndicator.Fill = (SolidColorBrush)FindResource("HighlightBrush");
                StatusText.Text = "Error";
            }
        }

        private async Task ResolvePublicEndpointsAsync()
        {
            var httpOverride = Environment.GetEnvironmentVariable("OCTOLINK_PUBLIC_HTTP_URL");
            var wsOverride = Environment.GetEnvironmentVariable("OCTOLINK_PUBLIC_WS_URL");

            if (!string.IsNullOrWhiteSpace(httpOverride))
            {
                _publicHttpUrl = httpOverride.Trim();
            }

            if (!string.IsNullOrWhiteSpace(wsOverride))
            {
                _websocketConnectUrl = wsOverride.Trim();
            }

            if (!string.IsNullOrWhiteSpace(_publicHttpUrl) && !string.IsNullOrWhiteSpace(_websocketConnectUrl))
            {
                return;
            }

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                var json = await client.GetStringAsync("http://127.0.0.1:4040/api/tunnels");
                using var doc = JsonDocument.Parse(json);

                string? httpUrl = null;
                string? wsUrl = null;

                if (doc.RootElement.TryGetProperty("tunnels", out var tunnels))
                {
                    foreach (var tunnel in tunnels.EnumerateArray())
                    {
                        var publicUrl = tunnel.TryGetProperty("public_url", out var pub) ? pub.GetString() : null;
                        var config = tunnel.TryGetProperty("config", out var cfg) ? cfg : default;
                        var addr = config.ValueKind == JsonValueKind.Object && config.TryGetProperty("addr", out var addrElem)
                            ? addrElem.GetString()
                            : null;

                        if (string.IsNullOrWhiteSpace(publicUrl) || string.IsNullOrWhiteSpace(addr))
                            continue;

                        if (addr.EndsWith($":{_port}", StringComparison.OrdinalIgnoreCase))
                        {
                            httpUrl ??= publicUrl;
                        }

                        if (addr.EndsWith($":{_webSocketPort}", StringComparison.OrdinalIgnoreCase))
                        {
                            wsUrl ??= publicUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                                ? "wss://" + publicUrl[8..]
                                : publicUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                                    ? "ws://" + publicUrl[7..]
                                    : publicUrl;
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(httpUrl))
                {
                    _publicHttpUrl = httpUrl;
                }

                if (!string.IsNullOrWhiteSpace(wsUrl))
                {
                    _websocketConnectUrl = wsUrl;
                }
            }
            catch
            {
                // Ignore ngrok detection failures and keep local URLs.
            }

            if (string.IsNullOrWhiteSpace(_publicHttpUrl))
            {
                _publicHttpUrl = _httpServer?.IsLoopbackOnly == true ? $"http://localhost:{_port}" : $"http://{_localIp}:{_port}";
            }

            if (string.IsNullOrWhiteSpace(_websocketConnectUrl))
            {
                _websocketConnectUrl = _webSocketServer?.IsLoopbackOnly == true ? $"ws://localhost:{_webSocketPort}/" : $"ws://{_localIp}:{_webSocketPort}/";
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopServer();
        }

        private void StopServer()
        {
            try
            {
                StopNgrokProcesses();
                _webSocketServer?.Stop();
                _httpServer?.Stop();
                _controllerManager?.Dispose();

                _webSocketServer = null;
                _httpServer = null;
                _controllerManager = null;

                StatusIndicator.Fill = (SolidColorBrush)FindResource("WarningBrush");
                StatusText.Text = "Stopped";
                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                LaunchNgrokButton.IsEnabled = false;
                NgrokStatusText.Text = "";

                // Reset controller displays
                foreach (var display in _controllerDisplays)
                {
                    display.SetDisconnected();
                }
                UpdateConnectedCount();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error stopping server: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void UpdateUI(ControllerUpdateEvent updateEvent)
        {
            Dispatcher.Invoke(() =>
            {
                switch (updateEvent.Type)
                {
                    case UpdateEventType.Connected:
                        _controllerDisplays[updateEvent.ControllerIndex].SetConnected(updateEvent.PlayerName);
                        break;
                    case UpdateEventType.Disconnected:
                        _controllerDisplays[updateEvent.ControllerIndex].SetDisconnected();
                        break;
                    case UpdateEventType.Input:
                        _controllerDisplays[updateEvent.ControllerIndex].UpdateInputs(updateEvent.State!);
                        break;
                }
                UpdateConnectedCount();
            });
        }

        private void UpdateConnectedCount()
        {
            int count = _controllerDisplays.Count(d => d.IsConnected);
            ConnectedCountText.Text = $"{count} / {VirtualControllerManager.MaxControllers}";
            ApplyLayoutButton.IsEnabled = _webSocketServer != null && count > 0;
        }

        private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingPreset) return;
            
            var presets = ControllerLayout.GetPresets();
            int index = PresetComboBox.SelectedIndex;
            
            if (index >= 0 && index < presets.Length)
            {
                var preset = presets[index];
                _isUpdatingPreset = true;
                
                ChkDPad.IsChecked = preset.ShowDPad;
                ChkFaceButtons.IsChecked = preset.ShowFaceButtons;
                ChkLeftStick.IsChecked = preset.ShowLeftStick;
                ChkRightStick.IsChecked = preset.ShowRightStick;
                ChkBumpers.IsChecked = preset.ShowBumpers;
                ChkTriggers.IsChecked = preset.ShowTriggers;
                ChkStartBack.IsChecked = preset.ShowStartBack;
                ChkGuide.IsChecked = preset.ShowGuide;
                
                _currentLayout = preset;
                _isUpdatingPreset = false;
            }
        }

        private void LayoutCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            // Don't process during initialization or preset updates
            if (_isUpdatingPreset || !IsLoaded || ChkDPad == null) return;
            
            // Update current layout from checkboxes
            _currentLayout = new ControllerLayout
            {
                Name = "Custom",
                ShowDPad = ChkDPad.IsChecked ?? true,
                ShowFaceButtons = ChkFaceButtons.IsChecked ?? true,
                ShowLeftStick = ChkLeftStick.IsChecked ?? true,
                ShowRightStick = ChkRightStick.IsChecked ?? true,
                ShowBumpers = ChkBumpers.IsChecked ?? true,
                ShowTriggers = ChkTriggers.IsChecked ?? true,
                ShowStartBack = ChkStartBack.IsChecked ?? true,
                ShowGuide = ChkGuide.IsChecked ?? true
            };
            
            // Switch to Custom preset
            _isUpdatingPreset = true;
            PresetComboBox.SelectedIndex = PresetComboBox.Items.Count - 1; // Custom is last
            _isUpdatingPreset = false;
            
            // Auto-save settings
            SaveSettings();
            
            // Auto-broadcast layout to all connected clients
            if (_webSocketServer != null)
            {
                _webSocketServer.BroadcastLayout(_currentLayout);
                LayoutStatusText.Text = $"Layout applied: {_currentLayout.Name}";
            }
        }
        
        private void InversionCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            // Don't process during initialization
            if (_isUpdatingPreset || !IsLoaded) return;
            
            // Apply axis inversion settings to controller manager
            ApplyAxisInversionSettings();
            
            // Save settings
            SaveSettings();
        }
        
        private void ApplyAxisInversionSettings()
        {
            if (_controllerManager == null) return;
            
            _controllerManager.InvertLeftX = ChkInvertLeftX.IsChecked ?? false;
            _controllerManager.InvertLeftY = ChkInvertLeftY.IsChecked ?? false;
            _controllerManager.InvertRightX = ChkInvertRightX.IsChecked ?? false;
            _controllerManager.InvertRightY = ChkInvertRightY.IsChecked ?? false;
        }
        
        private void LoadSavedSettings()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonSerializer.Deserialize<SavedSettings>(json);
                    if (settings != null)
                    {
                        _isUpdatingPreset = true;
                        ChkDPad.IsChecked = settings.ShowDPad;
                        ChkFaceButtons.IsChecked = settings.ShowFaceButtons;
                        ChkLeftStick.IsChecked = settings.ShowLeftStick;
                        ChkRightStick.IsChecked = settings.ShowRightStick;
                        ChkBumpers.IsChecked = settings.ShowBumpers;
                        ChkTriggers.IsChecked = settings.ShowTriggers;
                        ChkStartBack.IsChecked = settings.ShowStartBack;
                        ChkGuide.IsChecked = settings.ShowGuide;
                        PresetComboBox.SelectedIndex = settings.PresetIndex;
                        
                        // Load axis inversion settings
                        ChkInvertLeftX.IsChecked = settings.InvertLeftX;
                        ChkInvertLeftY.IsChecked = settings.InvertLeftY;
                        ChkInvertRightX.IsChecked = settings.InvertRightX;
                        ChkInvertRightY.IsChecked = settings.InvertRightY;
                        
                        _currentLayout = new ControllerLayout
                        {
                            Name = settings.PresetIndex == PresetComboBox.Items.Count - 1 ? "Custom" : PresetComboBox.Items[settings.PresetIndex]?.ToString() ?? "Full",
                            ShowDPad = settings.ShowDPad,
                            ShowFaceButtons = settings.ShowFaceButtons,
                            ShowLeftStick = settings.ShowLeftStick,
                            ShowRightStick = settings.ShowRightStick,
                            ShowBumpers = settings.ShowBumpers,
                            ShowTriggers = settings.ShowTriggers,
                            ShowStartBack = settings.ShowStartBack,
                            ShowGuide = settings.ShowGuide
                        };
                        _isUpdatingPreset = false;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
            }
        }
        
        private void SaveSettings()
        {
            try
            {
                var settings = new SavedSettings
                {
                    ShowDPad = ChkDPad.IsChecked ?? true,
                    ShowFaceButtons = ChkFaceButtons.IsChecked ?? true,
                    ShowLeftStick = ChkLeftStick.IsChecked ?? true,
                    ShowRightStick = ChkRightStick.IsChecked ?? true,
                    ShowBumpers = ChkBumpers.IsChecked ?? true,
                    ShowTriggers = ChkTriggers.IsChecked ?? true,
                    ShowStartBack = ChkStartBack.IsChecked ?? true,
                    ShowGuide = ChkGuide.IsChecked ?? true,
                    PresetIndex = PresetComboBox.SelectedIndex,
                    InvertLeftX = ChkInvertLeftX.IsChecked ?? false,
                    InvertLeftY = ChkInvertLeftY.IsChecked ?? false,
                    InvertRightX = ChkInvertRightX.IsChecked ?? false,
                    InvertRightY = ChkInvertRightY.IsChecked ?? false
                };
                
                var dir = Path.GetDirectoryName(SettingsFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }
        
        private class SavedSettings
        {
            public bool ShowDPad { get; set; } = true;
            public bool ShowFaceButtons { get; set; } = true;
            public bool ShowLeftStick { get; set; } = true;
            public bool ShowRightStick { get; set; } = true;
            public bool ShowBumpers { get; set; } = true;
            public bool ShowTriggers { get; set; } = true;
            public bool ShowStartBack { get; set; } = true;
            public bool ShowGuide { get; set; } = true;
            public int PresetIndex { get; set; } = 0;
            
            // Axis inversion settings
            public bool InvertLeftX { get; set; } = false;
            public bool InvertLeftY { get; set; } = false;
            public bool InvertRightX { get; set; } = false;
            public bool InvertRightY { get; set; } = false;
        }

        private void ApplyLayoutButton_Click(object sender, RoutedEventArgs e)
        {
            if (_webSocketServer == null) return;
            
            _webSocketServer.BroadcastLayout(_currentLayout);
            LayoutStatusText.Text = $"Layout applied: {_currentLayout.Name}";
        }

        private void LaunchNgrokButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_ngrokHttpProcess != null || _ngrokWsProcess != null)
                {
                    NgrokStatusText.Text = "ngrok is already running.";
                    return;
                }

                var ngrokExe = FindNgrokExecutable();
                if (string.IsNullOrWhiteSpace(ngrokExe))
                {
                    MessageBox.Show("Could not find ngrok.exe. Put it in PATH or next to Octolink.exe.", "ngrok not found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _ngrokHttpProcess = StartNgrokProcess(ngrokExe, $"http {_port}");
                _ngrokWsProcess = StartNgrokProcess(ngrokExe, $"http {_webSocketPort}");
                NgrokStatusText.Text = "ngrok tunnels launching... wait a few seconds, then reconnect using the public URL.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to launch ngrok: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private Process StartNgrokProcess(string ngrokExe, string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = ngrokExe,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
            };

            var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start ngrok.");
            return process;
        }

        private string? FindNgrokExecutable()
        {
            var envPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in envPath.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(dir.Trim(), "ngrok.exe");
                if (File.Exists(candidate)) return candidate;
            }

            var localCandidate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ngrok.exe");
            return File.Exists(localCandidate) ? localCandidate : null;
        }

        private void StopNgrokProcesses()
        {
            try { _ngrokHttpProcess?.Kill(true); } catch { }
            try { _ngrokWsProcess?.Kill(true); } catch { }
            _ngrokHttpProcess = null;
            _ngrokWsProcess = null;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            StopServer();
        }
    }
}
