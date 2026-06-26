using System.Collections.Generic;
using System.Windows.Input;
using Newtonsoft.Json;

namespace GameTracker.Models
{
    /// <summary>A single rebindable global hotkey (modifiers + key).</summary>
    public class HotkeyBinding
    {
        public ModifierKeys Modifiers { get; set; }
        public Key Key { get; set; }

        public HotkeyBinding() { }
        public HotkeyBinding(ModifierKeys modifiers, Key key)
        {
            Modifiers = modifiers;
            Key = key;
        }

        // Win32 RegisterHotKey modifier flags.
        [JsonIgnore]
        public uint Win32Modifiers
        {
            get
            {
                uint m = 0;
                if (Modifiers.HasFlag(ModifierKeys.Alt)) m |= 0x0001;
                if (Modifiers.HasFlag(ModifierKeys.Control)) m |= 0x0002;
                if (Modifiers.HasFlag(ModifierKeys.Shift)) m |= 0x0004;
                if (Modifiers.HasFlag(ModifierKeys.Windows)) m |= 0x0008;
                return m;
            }
        }

        [JsonIgnore]
        public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

        [JsonIgnore]
        public string Display
        {
            get
            {
                var parts = new List<string>();
                if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
                if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
                if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
                if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
                parts.Add(Key.ToString());
                return string.Join(" + ", parts);
            }
        }
    }

    public class HotkeyConfig
    {
        public HotkeyBinding Toggle { get; set; } = new(ModifierKeys.Control | ModifierKeys.Alt, Key.S);
        public HotkeyBinding Clip { get; set; } = new(ModifierKeys.Control | ModifierKeys.Alt, Key.C);
        public HotkeyBinding Note { get; set; } = new(ModifierKeys.Control | ModifierKeys.Alt, Key.N);
    }
}
