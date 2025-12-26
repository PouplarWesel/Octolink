using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;
using Nefarius.ViGEm.Client.Targets.DualShock4;

namespace VirtualControllerServer
{
    /// <summary>
    /// Manages up to 8 virtual controllers using ViGEmBus
    /// Slots 1-4: Xbox 360 controllers (XInput - works with all games)
    /// Slots 5-8: DualShock 4 controllers (works through Steam Input for games like PlateUp!)
    /// </summary>
    public class VirtualControllerManager : IDisposable
    {
        public const int MaxControllers = 8;
        public const int XboxControllerCount = 4; // Slots 0-3 are Xbox, 4-7 are DualShock 4
        
        private readonly ViGEmClient _client;
        private readonly IXbox360Controller?[] _xboxControllers = new IXbox360Controller?[XboxControllerCount];
        private readonly IDualShock4Controller?[] _ds4Controllers = new IDualShock4Controller?[MaxControllers - XboxControllerCount];
        private readonly bool[] _controllersActive = new bool[MaxControllers];
        private readonly object _lock = new();
        private bool _disposed;

        public VirtualControllerManager()
        {
            _client = new ViGEmClient();
        }

        /// <summary>
        /// Returns true if the slot uses DualShock 4 (slots 4-7), false for Xbox 360 (slots 0-3)
        /// </summary>
        public static bool IsDualShock4Slot(int index) => index >= XboxControllerCount;

        /// <summary>
        /// Gets the controller type name for UI display
        /// </summary>
        public static string GetControllerTypeName(int index) => IsDualShock4Slot(index) ? "DualShock 4" : "Xbox 360";

        /// <summary>
        /// Creates and connects a virtual controller at the specified index
        /// </summary>
        public bool CreateController(int index)
        {
            if (index < 0 || index >= MaxControllers) return false;

            lock (_lock)
            {
                if (_controllersActive[index]) return true; // Already exists

                try
                {
                    if (IsDualShock4Slot(index))
                    {
                        // Create DualShock 4 controller for slots 4-7
                        int ds4Index = index - XboxControllerCount;
                        var controller = _client.CreateDualShock4Controller();
                        controller.Connect();
                        _ds4Controllers[ds4Index] = controller;
                        Console.WriteLine($"Created DualShock 4 controller at slot {index + 1}");
                    }
                    else
                    {
                        // Create Xbox 360 controller for slots 0-3
                        var controller = _client.CreateXbox360Controller();
                        controller.Connect();
                        _xboxControllers[index] = controller;
                        Console.WriteLine($"Created Xbox 360 controller at slot {index + 1}");
                    }
                    
                    _controllersActive[index] = true;
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to create controller {index}: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Disconnects and removes a virtual controller at the specified index
        /// </summary>
        public void RemoveController(int index)
        {
            if (index < 0 || index >= MaxControllers) return;

            lock (_lock)
            {
                try
                {
                    if (IsDualShock4Slot(index))
                    {
                        int ds4Index = index - XboxControllerCount;
                        if (_ds4Controllers[ds4Index] != null)
                        {
                            _ds4Controllers[ds4Index]!.Disconnect();
                            _ds4Controllers[ds4Index] = null;
                        }
                    }
                    else
                    {
                        if (_xboxControllers[index] != null)
                        {
                            _xboxControllers[index]!.Disconnect();
                            _xboxControllers[index] = null;
                        }
                    }
                }
                catch { }
                
                _controllersActive[index] = false;
            }
        }

        /// <summary>
        /// Updates the state of a virtual controller
        /// </summary>
        public void UpdateController(int index, ControllerState state)
        {
            if (index < 0 || index >= MaxControllers) return;

            lock (_lock)
            {
                if (!_controllersActive[index]) return;

                try
                {
                    if (IsDualShock4Slot(index))
                    {
                        UpdateDualShock4Controller(index - XboxControllerCount, state);
                    }
                    else
                    {
                        UpdateXbox360Controller(index, state);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error updating controller {index}: {ex.Message}");
                }
            }
        }

        private void UpdateXbox360Controller(int index, ControllerState state)
        {
            var controller = _xboxControllers[index];
            if (controller == null) return;

            // Set buttons
            controller.SetButtonState(Xbox360Button.A, state.ButtonA);
            controller.SetButtonState(Xbox360Button.B, state.ButtonB);
            controller.SetButtonState(Xbox360Button.X, state.ButtonX);
            controller.SetButtonState(Xbox360Button.Y, state.ButtonY);
            
            controller.SetButtonState(Xbox360Button.LeftShoulder, state.LeftBumper);
            controller.SetButtonState(Xbox360Button.RightShoulder, state.RightBumper);
            
            controller.SetButtonState(Xbox360Button.Back, state.Back);
            controller.SetButtonState(Xbox360Button.Start, state.Start);
            controller.SetButtonState(Xbox360Button.Guide, state.Guide);
            
            controller.SetButtonState(Xbox360Button.LeftThumb, state.LeftStickClick);
            controller.SetButtonState(Xbox360Button.RightThumb, state.RightStickClick);
            
            // D-Pad
            controller.SetButtonState(Xbox360Button.Up, state.DPadUp);
            controller.SetButtonState(Xbox360Button.Down, state.DPadDown);
            controller.SetButtonState(Xbox360Button.Left, state.DPadLeft);
            controller.SetButtonState(Xbox360Button.Right, state.DPadRight);

            // Triggers (0-255)
            controller.SetSliderValue(Xbox360Slider.LeftTrigger, (byte)(state.LeftTrigger * 255));
            controller.SetSliderValue(Xbox360Slider.RightTrigger, (byte)(state.RightTrigger * 255));

            // Analog sticks (-32768 to 32767)
            controller.SetAxisValue(Xbox360Axis.LeftThumbX, (short)(state.LeftStickX * 32767));
            controller.SetAxisValue(Xbox360Axis.LeftThumbY, (short)(state.LeftStickY * 32767));
            controller.SetAxisValue(Xbox360Axis.RightThumbX, (short)(state.RightStickX * 32767));
            controller.SetAxisValue(Xbox360Axis.RightThumbY, (short)(state.RightStickY * 32767));

            controller.SubmitReport();
        }

        private void UpdateDualShock4Controller(int ds4Index, ControllerState state)
        {
            var controller = _ds4Controllers[ds4Index];
            if (controller == null) return;

            // Map Xbox buttons to DualShock 4 buttons
            // A -> Cross, B -> Circle, X -> Square, Y -> Triangle
            controller.SetButtonState(DualShock4Button.Cross, state.ButtonA);
            controller.SetButtonState(DualShock4Button.Circle, state.ButtonB);
            controller.SetButtonState(DualShock4Button.Square, state.ButtonX);
            controller.SetButtonState(DualShock4Button.Triangle, state.ButtonY);
            
            // LB -> L1, RB -> R1
            controller.SetButtonState(DualShock4Button.ShoulderLeft, state.LeftBumper);
            controller.SetButtonState(DualShock4Button.ShoulderRight, state.RightBumper);
            
            // Back -> Share, Start -> Options
            controller.SetButtonState(DualShock4Button.Share, state.Back);
            controller.SetButtonState(DualShock4Button.Options, state.Start);
            
            // Stick clicks -> L3/R3
            controller.SetButtonState(DualShock4Button.ThumbLeft, state.LeftStickClick);
            controller.SetButtonState(DualShock4Button.ThumbRight, state.RightStickClick);
            
            // D-Pad - DualShock 4 uses a different D-pad system
            controller.SetDPadDirection(GetDPadDirection(state));

            // Triggers (0-255) - L2/R2
            controller.SetSliderValue(DualShock4Slider.LeftTrigger, (byte)(state.LeftTrigger * 255));
            controller.SetSliderValue(DualShock4Slider.RightTrigger, (byte)(state.RightTrigger * 255));

            // Analog sticks (0-255, 128 is center)
            controller.SetAxisValue(DualShock4Axis.LeftThumbX, (byte)((state.LeftStickX + 1) * 127.5f));
            controller.SetAxisValue(DualShock4Axis.LeftThumbY, (byte)((1 - state.LeftStickY) * 127.5f)); // Y is inverted
            controller.SetAxisValue(DualShock4Axis.RightThumbX, (byte)((state.RightStickX + 1) * 127.5f));
            controller.SetAxisValue(DualShock4Axis.RightThumbY, (byte)((1 - state.RightStickY) * 127.5f)); // Y is inverted

            controller.SubmitReport();
        }

        private static DualShock4DPadDirection GetDPadDirection(ControllerState state)
        {
            bool up = state.DPadUp;
            bool down = state.DPadDown;
            bool left = state.DPadLeft;
            bool right = state.DPadRight;

            if (up && right) return DualShock4DPadDirection.Northeast;
            if (up && left) return DualShock4DPadDirection.Northwest;
            if (down && right) return DualShock4DPadDirection.Southeast;
            if (down && left) return DualShock4DPadDirection.Southwest;
            if (up) return DualShock4DPadDirection.North;
            if (down) return DualShock4DPadDirection.South;
            if (left) return DualShock4DPadDirection.West;
            if (right) return DualShock4DPadDirection.East;
            return DualShock4DPadDirection.None;
        }

        /// <summary>
        /// Finds the next available controller slot
        /// </summary>
        public int GetNextAvailableSlot()
        {
            lock (_lock)
            {
                for (int i = 0; i < MaxControllers; i++)
                {
                    if (!_controllersActive[i])
                    {
                        return i;
                    }
                }
                return -1; // No slots available
            }
        }

        /// <summary>
        /// Checks if a specific slot is available
        /// </summary>
        public bool IsSlotAvailable(int index)
        {
            if (index < 0 || index >= MaxControllers) return false;
            lock (_lock)
            {
                return !_controllersActive[index];
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            lock (_lock)
            {
                // Dispose Xbox 360 controllers
                for (int i = 0; i < XboxControllerCount; i++)
                {
                    if (_xboxControllers[i] != null)
                    {
                        try
                        {
                            _xboxControllers[i]!.Disconnect();
                        }
                        catch { }
                        _xboxControllers[i] = null;
                    }
                }
                
                // Dispose DualShock 4 controllers
                for (int i = 0; i < _ds4Controllers.Length; i++)
                {
                    if (_ds4Controllers[i] != null)
                    {
                        try
                        {
                            _ds4Controllers[i]!.Disconnect();
                        }
                        catch { }
                        _ds4Controllers[i] = null;
                    }
                }
            }

            _client.Dispose();
        }
    }

    /// <summary>
    /// Represents the state of a controller's inputs
    /// </summary>
    public class ControllerState
    {
        // Face buttons
        public bool ButtonA { get; set; }
        public bool ButtonB { get; set; }
        public bool ButtonX { get; set; }
        public bool ButtonY { get; set; }

        // Bumpers
        public bool LeftBumper { get; set; }
        public bool RightBumper { get; set; }

        // Menu buttons
        public bool Back { get; set; }
        public bool Start { get; set; }
        public bool Guide { get; set; }

        // Stick clicks
        public bool LeftStickClick { get; set; }
        public bool RightStickClick { get; set; }

        // D-Pad
        public bool DPadUp { get; set; }
        public bool DPadDown { get; set; }
        public bool DPadLeft { get; set; }
        public bool DPadRight { get; set; }

        // Triggers (0.0 to 1.0)
        public float LeftTrigger { get; set; }
        public float RightTrigger { get; set; }

        // Analog sticks (-1.0 to 1.0)
        public float LeftStickX { get; set; }
        public float LeftStickY { get; set; }
        public float RightStickX { get; set; }
        public float RightStickY { get; set; }
    }
}
