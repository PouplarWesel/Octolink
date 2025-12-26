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

namespace VirtualControllerServer
{
    public partial class MainWindow : Window
    {
        private WebSocketServer? _webSocketServer;
        private HttpServer? _httpServer;
        private VirtualControllerManager? _controllerManager;
        private readonly ControllerDisplay[] _controllerDisplays = new ControllerDisplay[VirtualControllerManager.MaxControllers];
        private readonly int _port = 5000;
        private string _localIp = "";
        private ControllerLayout _currentLayout = new();
        private bool _isUpdatingPreset = false;
        private static readonly string SettingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VirtualControllerServer",
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
                
                // Start HTTP server for serving the web interface
                _httpServer = new HttpServer(_port, _localIp);
                await _httpServer.StartAsync();

                // Start WebSocket server for real-time communication
                _webSocketServer = new WebSocketServer(_port + 1, _localIp, _controllerManager, UpdateUI);
                await _webSocketServer.StartAsync();
                
                // Set initial layout so new connections get it
                _webSocketServer.SetCurrentLayout(_currentLayout);

                // Generate QR code
                string url = $"http://{_localIp}:{_port}";
                GenerateQRCode(url);

                // Update status
                StatusIndicator.Fill = (SolidColorBrush)FindResource("SuccessBrush");
                StatusText.Text = "Running";
                StopButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start server: {ex.Message}\n\nMake sure ViGEmBus driver is installed.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StartButton.IsEnabled = true;
                StatusIndicator.Fill = (SolidColorBrush)FindResource("HighlightBrush");
                StatusText.Text = "Error";
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
                    PresetIndex = PresetComboBox.SelectedIndex
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
        }

        private void ApplyLayoutButton_Click(object sender, RoutedEventArgs e)
        {
            if (_webSocketServer == null) return;
            
            _webSocketServer.BroadcastLayout(_currentLayout);
            LayoutStatusText.Text = $"Layout applied: {_currentLayout.Name}";
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            StopServer();
        }
    }
}
