using System.Text.Json;
using System.Text.Json.Serialization;

namespace VirtualControllerServer
{
    /// <summary>
    /// Defines which controller elements are visible on the phone
    /// </summary>
    public class ControllerLayout
    {
        public string Name { get; set; } = "Default";
        
        // Control groups
        public bool ShowDPad { get; set; } = true;
        public bool ShowFaceButtons { get; set; } = true;
        public bool ShowLeftStick { get; set; } = true;
        public bool ShowRightStick { get; set; } = true;
        public bool ShowBumpers { get; set; } = true;
        public bool ShowTriggers { get; set; } = true;
        public bool ShowStartBack { get; set; } = true;
        public bool ShowGuide { get; set; } = true;

        public string ToJson()
        {
            return JsonSerializer.Serialize(new
            {
                type = "layout",
                name = Name,
                dpad = ShowDPad,
                face = ShowFaceButtons,
                lstick = ShowLeftStick,
                rstick = ShowRightStick,
                bumpers = ShowBumpers,
                triggers = ShowTriggers,
                startback = ShowStartBack,
                guide = ShowGuide
            });
        }

        public static ControllerLayout[] GetPresets()
        {
            return new[]
            {
                new ControllerLayout { Name = "Full Controller" },
                
                new ControllerLayout 
                { 
                    Name = "Simple (Face + D-Pad)",
                    ShowLeftStick = false,
                    ShowRightStick = false,
                    ShowTriggers = false,
                    ShowBumpers = false,
                    ShowGuide = false
                },
                
                new ControllerLayout 
                { 
                    Name = "Racing (Sticks + Triggers)",
                    ShowDPad = false,
                    ShowFaceButtons = false,
                    ShowBumpers = false,
                    ShowStartBack = false,
                    ShowGuide = false
                },
                
                new ControllerLayout 
                { 
                    Name = "Platformer (D-Pad + Face)",
                    ShowLeftStick = false,
                    ShowRightStick = false,
                    ShowTriggers = false,
                    ShowGuide = false
                },
                
                new ControllerLayout 
                { 
                    Name = "Twin Stick Shooter",
                    ShowDPad = false,
                    ShowTriggers = false,
                    ShowBumpers = false,
                    ShowStartBack = false,
                    ShowGuide = false
                },
                
                new ControllerLayout 
                { 
                    Name = "FPS (Sticks + Triggers + Bumpers)",
                    ShowDPad = false,
                    ShowStartBack = false,
                    ShowGuide = false
                },
                
                new ControllerLayout 
                { 
                    Name = "Face Buttons Only",
                    ShowDPad = false,
                    ShowLeftStick = false,
                    ShowRightStick = false,
                    ShowTriggers = false,
                    ShowBumpers = false,
                    ShowStartBack = false,
                    ShowGuide = false
                },
                
                new ControllerLayout 
                { 
                    Name = "Custom",
                }
            };
        }
    }
}
