using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace VirtualControllerServer
{
    /// <summary>
    /// Custom control for displaying a single controller's status and inputs
    /// </summary>
    public class ControllerDisplay : Border
    {
        private readonly int _controllerNumber;
        private readonly TextBlock _statusText;
        private readonly TextBlock _playerNameText;
        private readonly Ellipse _connectionIndicator;
        private readonly Button _kickButton;
        
        // Button indicators
        private readonly Ellipse _btnA, _btnB, _btnX, _btnY;
        private readonly Ellipse _btnLB, _btnRB;
        private readonly Ellipse _btnBack, _btnStart, _btnGuide;
        private readonly Ellipse _dpadUp, _dpadDown, _dpadLeft, _dpadRight;
        
        // Trigger bars
        private readonly Rectangle _ltBar, _rtBar;
        
        // Stick positions
        private readonly Ellipse _leftStickDot, _rightStickDot;
        private readonly Border _leftStickArea, _rightStickArea;

        public bool IsConnected { get; private set; }
        public int ControllerIndex => _controllerNumber - 1;
        
        // Event for kick button
        public event EventHandler<int>? KickRequested;

        private static readonly SolidColorBrush InactiveBrush = new(Color.FromRgb(60, 60, 80));
        private static readonly SolidColorBrush ActiveBrush = new(Color.FromRgb(233, 69, 96)); // Highlight color
        private static readonly SolidColorBrush ConnectedBrush = new(Color.FromRgb(78, 204, 163)); // Success color
        private static readonly SolidColorBrush ButtonABrush = new(Color.FromRgb(76, 175, 80)); // Green
        private static readonly SolidColorBrush ButtonBBrush = new(Color.FromRgb(244, 67, 54)); // Red  
        private static readonly SolidColorBrush ButtonXBrush = new(Color.FromRgb(33, 150, 243)); // Blue
        private static readonly SolidColorBrush ButtonYBrush = new(Color.FromRgb(255, 193, 7)); // Yellow

        public ControllerDisplay(int controllerNumber)
        {
            _controllerNumber = controllerNumber;
            
            // Main container styling
            Background = new SolidColorBrush(Color.FromRgb(15, 52, 96));
            CornerRadius = new CornerRadius(12);
            Margin = new Thickness(8);
            Width = 260;
            Height = 320;
            
            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            
            // Header
            var header = new StackPanel { Margin = new Thickness(12, 12, 12, 8) };
            
            var headerRow = new StackPanel { Orientation = Orientation.Horizontal };
            _connectionIndicator = new Ellipse { Width = 12, Height = 12, Fill = InactiveBrush, Margin = new Thickness(0, 0, 8, 0) };
            
            // Show controller type (Xbox vs PS4)
            string controllerType = VirtualControllerManager.GetControllerTypeName(_controllerNumber - 1);
            string typeColor = VirtualControllerManager.IsDualShock4Slot(_controllerNumber - 1) ? "#003791" : "#107c10";
            var numberText = new TextBlock { Text = $"Controller {_controllerNumber}", FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
            var typeText = new TextBlock 
            { 
                Text = controllerType, 
                FontSize = 10, 
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(typeColor)),
                Margin = new Thickness(8, 3, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            headerRow.Children.Add(_connectionIndicator);
            headerRow.Children.Add(numberText);
            headerRow.Children.Add(typeText);
            
            // Kick button
            _kickButton = new Button
            {
                Content = "✕",
                Width = 24,
                Height = 24,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromRgb(244, 67, 54)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(8, 0, 0, 0),
                Visibility = Visibility.Collapsed,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Kick this controller"
            };
            _kickButton.Click += (s, e) => KickRequested?.Invoke(this, ControllerIndex);
            headerRow.Children.Add(_kickButton);
            
            _playerNameText = new TextBlock { Text = "", FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)), Margin = new Thickness(20, 2, 0, 0) };
            _statusText = new TextBlock { Text = "Disconnected", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)), Margin = new Thickness(20, 2, 0, 0) };
            
            header.Children.Add(headerRow);
            header.Children.Add(_playerNameText);
            header.Children.Add(_statusText);
            Grid.SetRow(header, 0);
            
            // Controller visualization
            var controllerArea = new Grid { Margin = new Thickness(12, 0, 12, 12) };
            
            // Create controller layout
            var canvas = new Canvas { Width = 236, Height = 200 };
            
            // D-Pad (left side)
            _dpadUp = CreateButton(30, 60, 20, InactiveBrush);
            _dpadDown = CreateButton(30, 100, 20, InactiveBrush);
            _dpadLeft = CreateButton(10, 80, 20, InactiveBrush);
            _dpadRight = CreateButton(50, 80, 20, InactiveBrush);
            canvas.Children.Add(_dpadUp);
            canvas.Children.Add(_dpadDown);
            canvas.Children.Add(_dpadLeft);
            canvas.Children.Add(_dpadRight);
            
            // Left stick area
            _leftStickArea = CreateStickArea(20, 130);
            _leftStickDot = new Ellipse { Width = 16, Height = 16, Fill = ActiveBrush };
            Canvas.SetLeft(_leftStickDot, 42);
            Canvas.SetTop(_leftStickDot, 152);
            canvas.Children.Add(_leftStickArea);
            canvas.Children.Add(_leftStickDot);
            
            // Face buttons (right side)
            _btnY = CreateButton(186, 60, 22, ButtonYBrush, "Y");
            _btnB = CreateButton(206, 80, 22, ButtonBBrush, "B");
            _btnA = CreateButton(186, 100, 22, ButtonABrush, "A");
            _btnX = CreateButton(166, 80, 22, ButtonXBrush, "X");
            canvas.Children.Add(_btnY);
            canvas.Children.Add(_btnB);
            canvas.Children.Add(_btnA);
            canvas.Children.Add(_btnX);
            
            // Right stick area
            _rightStickArea = CreateStickArea(166, 130);
            _rightStickDot = new Ellipse { Width = 16, Height = 16, Fill = ActiveBrush };
            Canvas.SetLeft(_rightStickDot, 188);
            Canvas.SetTop(_rightStickDot, 152);
            canvas.Children.Add(_rightStickArea);
            canvas.Children.Add(_rightStickDot);
            
            // Bumpers and triggers
            _btnLB = CreateButton(30, 20, 30, 18, InactiveBrush, "LB");
            _btnRB = CreateButton(176, 20, 30, 18, InactiveBrush, "RB");
            canvas.Children.Add(_btnLB);
            canvas.Children.Add(_btnRB);
            
            // Trigger bars
            _ltBar = CreateTriggerBar(20, 5);
            _rtBar = CreateTriggerBar(166, 5);
            canvas.Children.Add(_ltBar);
            canvas.Children.Add(_rtBar);
            
            // Center buttons
            _btnBack = CreateButton(90, 80, 16, InactiveBrush);
            _btnGuide = CreateButton(110, 80, 20, InactiveBrush);
            _btnStart = CreateButton(134, 80, 16, InactiveBrush);
            canvas.Children.Add(_btnBack);
            canvas.Children.Add(_btnGuide);
            canvas.Children.Add(_btnStart);
            
            controllerArea.Children.Add(canvas);
            Grid.SetRow(controllerArea, 1);
            
            mainGrid.Children.Add(header);
            mainGrid.Children.Add(controllerArea);
            
            Child = mainGrid;
        }

        private static Ellipse CreateButton(double left, double top, double size, SolidColorBrush fill, string? label = null)
        {
            var btn = new Ellipse { Width = size, Height = size, Fill = fill, Opacity = 0.5 };
            Canvas.SetLeft(btn, left);
            Canvas.SetTop(btn, top);
            return btn;
        }

        private static Ellipse CreateButton(double left, double top, double width, double height, SolidColorBrush fill, string? label = null)
        {
            var btn = new Ellipse { Width = width, Height = height, Fill = fill, Opacity = 0.5 };
            Canvas.SetLeft(btn, left);
            Canvas.SetTop(btn, top);
            return btn;
        }

        private static Border CreateStickArea(double left, double top)
        {
            var area = new Border
            {
                Width = 60,
                Height = 60,
                Background = new SolidColorBrush(Color.FromRgb(40, 40, 60)),
                CornerRadius = new CornerRadius(30),
                BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 100)),
                BorderThickness = new Thickness(2)
            };
            Canvas.SetLeft(area, left);
            Canvas.SetTop(area, top);
            return area;
        }

        private static Rectangle CreateTriggerBar(double left, double top)
        {
            var bar = new Rectangle
            {
                Width = 50,
                Height = 10,
                Fill = InactiveBrush,
                RadiusX = 3,
                RadiusY = 3
            };
            Canvas.SetLeft(bar, left);
            Canvas.SetTop(bar, top);
            return bar;
        }

        public void SetConnected(string? playerName)
        {
            IsConnected = true;
            _connectionIndicator.Fill = ConnectedBrush;
            _playerNameText.Text = playerName ?? "Player";
            _statusText.Text = "Connected";
            _statusText.Foreground = ConnectedBrush;
            _kickButton.Visibility = Visibility.Visible;
        }

        public void SetDisconnected()
        {
            IsConnected = false;
            _connectionIndicator.Fill = InactiveBrush;
            _playerNameText.Text = "";
            _statusText.Text = "Disconnected";
            _statusText.Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150));
            _kickButton.Visibility = Visibility.Collapsed;
            
            // Reset all inputs
            UpdateInputs(new ControllerState());
        }

        public void UpdateInputs(ControllerState state)
        {
            // Face buttons
            SetButtonState(_btnA, state.ButtonA);
            SetButtonState(_btnB, state.ButtonB);
            SetButtonState(_btnX, state.ButtonX);
            SetButtonState(_btnY, state.ButtonY);
            
            // Bumpers
            SetButtonState(_btnLB, state.LeftBumper);
            SetButtonState(_btnRB, state.RightBumper);
            
            // Center buttons
            SetButtonState(_btnBack, state.Back);
            SetButtonState(_btnStart, state.Start);
            SetButtonState(_btnGuide, state.Guide);
            
            // D-Pad
            SetButtonState(_dpadUp, state.DPadUp);
            SetButtonState(_dpadDown, state.DPadDown);
            SetButtonState(_dpadLeft, state.DPadLeft);
            SetButtonState(_dpadRight, state.DPadRight);
            
            // Triggers
            UpdateTrigger(_ltBar, state.LeftTrigger);
            UpdateTrigger(_rtBar, state.RightTrigger);
            
            // Left stick
            UpdateStickPosition(_leftStickDot, _leftStickArea, state.LeftStickX, state.LeftStickY);
            
            // Right stick
            UpdateStickPosition(_rightStickDot, _rightStickArea, state.RightStickX, state.RightStickY);
        }

        private static void SetButtonState(Ellipse button, bool pressed)
        {
            button.Opacity = pressed ? 1.0 : 0.5;
        }

        private static void UpdateTrigger(Rectangle bar, float value)
        {
            bar.Fill = value > 0.1 ? ActiveBrush : InactiveBrush;
            bar.Width = 10 + (value * 40);
        }

        private static void UpdateStickPosition(Ellipse dot, Border area, float x, float y)
        {
            double areaLeft = Canvas.GetLeft(area);
            double areaTop = Canvas.GetTop(area);
            double centerX = areaLeft + 30 - 8;
            double centerY = areaTop + 30 - 8;
            
            // x and y are -1 to 1, map to pixel offset (max 20px from center)
            double offsetX = x * 20;
            double offsetY = -y * 20; // Invert Y for screen coordinates
            
            Canvas.SetLeft(dot, centerX + offsetX);
            Canvas.SetTop(dot, centerY + offsetY);
        }
    }
}
