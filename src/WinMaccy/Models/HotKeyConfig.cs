namespace WinMaccy.Models;

/// <summary>Global hotkey, expressed as modifiers + a Win32 virtual key code.</summary>
public sealed class HotKeyConfig
{
    public bool Ctrl { get; set; } = true;
    public bool Shift { get; set; } = true;
    public bool Alt { get; set; }
    public bool Win { get; set; }

    /// <summary>Win32 VK_* code; 'C' is 0x43.</summary>
    public int VirtualKey { get; set; } = 0x43;

    public bool IsValid =>
        VirtualKey >= 0x08 && VirtualKey <= 0xFE && (Ctrl || Alt || Shift || Win);

    public HotKeyConfig Clone() => new()
    {
        Ctrl = Ctrl,
        Shift = Shift,
        Alt = Alt,
        Win = Win,
        VirtualKey = VirtualKey
    };

    public string Display
    {
        get
        {
            var parts = new List<string>(4);
            if (Ctrl) parts.Add("Ctrl");
            if (Alt) parts.Add("Alt");
            if (Shift) parts.Add("Shift");
            if (Win) parts.Add("Win");
            parts.Add(VirtualKeyToText(VirtualKey));
            return string.Join(" + ", parts);
        }
    }

    public static string VirtualKeyToText(int vk)
    {
        if (vk is >= 0x30 and <= 0x5A) return ((char)vk).ToString();        // 0-9, A-Z
        if (vk is >= 0x60 and <= 0x69) return "Num" + (vk - 0x60);
        if (vk is >= 112 and <= 135) return "F" + (vk - 111);
        return vk switch
        {
            0x0D => "Enter",
            0x20 => "Space",
            0x14 => "Caps",
            0x09 => "Tab",
            0x2D => "Insert",
            0x2E => "Delete",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0xBA => ";",
            0xBB => "=",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            0xDE => "'",
            _ => "VK" + vk
        };
    }
}
