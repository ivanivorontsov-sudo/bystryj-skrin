using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace BystryjSkrin
{
    [Flags]
    internal enum Mods : uint { None = 0, Alt = 1, Ctrl = 2, Shift = 4, Win = 8 }

    internal struct Hotkey : IEquatable<Hotkey>
    {
        public readonly Mods Modifiers;
        public readonly Keys Key;

        public Hotkey(Mods modifiers, Keys key) { Modifiers = modifiers; Key = key & Keys.KeyCode; }

        public bool IsEmpty { get { return Key == Keys.None; } }

        public static readonly Hotkey DefaultCapture = new Hotkey(Mods.Ctrl | Mods.Shift, Keys.S);
        public static readonly Hotkey DefaultUndo = new Hotkey(Mods.Ctrl | Mods.Shift, Keys.Z);

        /// <summary>Запасные варианты, если основное сочетание занято.</summary>
        public static readonly Hotkey[] CaptureAlternatives =
        {
            new Hotkey(Mods.Ctrl | Mods.Alt, Keys.S),
            new Hotkey(Mods.Ctrl | Mods.Shift | Mods.Alt, Keys.S),
            new Hotkey(Mods.Ctrl | Mods.Alt, Keys.PrintScreen),
            new Hotkey(Mods.Ctrl | Mods.Shift, Keys.PrintScreen),
            new Hotkey(Mods.Alt | Mods.Shift, Keys.S),
            new Hotkey(Mods.Ctrl | Mods.Alt, Keys.Q),
            new Hotkey(Mods.Ctrl | Mods.Shift, Keys.F12),
            new Hotkey(Mods.None, Keys.F9),
        };

        public static readonly Hotkey[] UndoAlternatives =
        {
            new Hotkey(Mods.Ctrl | Mods.Alt, Keys.Z),
            new Hotkey(Mods.Ctrl | Mods.Shift | Mods.Alt, Keys.Z),
            new Hotkey(Mods.Ctrl | Mods.Shift, Keys.Back),
            new Hotkey(Mods.Ctrl | Mods.Alt, Keys.Back),
        };

        private static readonly Dictionary<Keys, string> Names = new Dictionary<Keys, string>
        {
            { Keys.D0, "0" }, { Keys.D1, "1" }, { Keys.D2, "2" }, { Keys.D3, "3" }, { Keys.D4, "4" },
            { Keys.D5, "5" }, { Keys.D6, "6" }, { Keys.D7, "7" }, { Keys.D8, "8" }, { Keys.D9, "9" },
            { Keys.PrintScreen, "PrintScreen" }, { Keys.PageUp, "PageUp" }, { Keys.PageDown, "PageDown" },
            { Keys.Enter, "Enter" }, { Keys.Back, "Backspace" }, { Keys.Escape, "Esc" }, { Keys.Delete, "Delete" },
            { Keys.Insert, "Insert" }, { Keys.Space, "Space" }, { Keys.Pause, "Pause" }, { Keys.Scroll, "ScrollLock" },
            { Keys.CapsLock, "CapsLock" },
        };

        public override string ToString()
        {
            if (IsEmpty) return "";
            var parts = new List<string>();
            if ((Modifiers & Mods.Ctrl) != 0) parts.Add("Ctrl");
            if ((Modifiers & Mods.Alt) != 0) parts.Add("Alt");
            if ((Modifiers & Mods.Shift) != 0) parts.Add("Shift");
            if ((Modifiers & Mods.Win) != 0) parts.Add("Win");
            parts.Add(KeyName(Key));
            return string.Join("+", parts);
        }

        public static string KeyName(Keys k)
        {
            string n;
            return Names.TryGetValue(k, out n) ? n : k.ToString();
        }

        public static bool TryParse(string text, out Hotkey result)
        {
            result = default(Hotkey);
            if (string.IsNullOrWhiteSpace(text)) return false;
            string[] parts = text.Split('+').Select(p => p.Trim()).ToArray();
            if (parts.Length == 0 || parts.Any(p => p.Length == 0))
            {
                // «Ctrl++» и подобное не поддерживаем
                return false;
            }
            Mods mods = Mods.None;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].ToLowerInvariant())
                {
                    case "ctrl": case "control": case "ctl": mods |= Mods.Ctrl; break;
                    case "alt": mods |= Mods.Alt; break;
                    case "shift": mods |= Mods.Shift; break;
                    case "win": case "windows": mods |= Mods.Win; break;
                    default: return false;
                }
            }
            string last = parts[parts.Length - 1];
            Keys key = Keys.None;
            foreach (var kv in Names)
                if (string.Equals(kv.Value, last, StringComparison.OrdinalIgnoreCase)) { key = kv.Key; break; }
            if (key == Keys.None)
            {
                if (last.Equals("PrtSc", StringComparison.OrdinalIgnoreCase)) key = Keys.PrintScreen;
                else
                {
                    Keys parsed;
                    if (!Enum.TryParse(last, true, out parsed) || int.TryParse(last, out _)) return false;
                    if ((parsed & Keys.Modifiers) != 0) return false; // «Shift», «Control», «Alt» — это не клавиша
                    key = parsed;
                }
            }
            if (IsModifierKey(key) || key == Keys.None) return false;
            result = new Hotkey(mods, key);
            return true;
        }

        public static bool IsModifierKey(Keys k)
        {
            k &= Keys.KeyCode;
            return k == Keys.ControlKey || k == Keys.LControlKey || k == Keys.RControlKey ||
                   k == Keys.ShiftKey || k == Keys.LShiftKey || k == Keys.RShiftKey ||
                   k == Keys.Menu || k == Keys.LMenu || k == Keys.RMenu ||
                   k == Keys.LWin || k == Keys.RWin || k == Keys.Apps;
        }

        private static bool IsStandaloneKey(Keys k)
        {
            return (k >= Keys.F1 && k <= Keys.F24) || k == Keys.PrintScreen || k == Keys.Pause || k == Keys.Scroll;
        }

        /// <summary>Проверка сочетания. null — всё в порядке, иначе понятное объяснение по-русски.</summary>
        public static string Validate(Hotkey h)
        {
            if (h.IsEmpty) return "Сочетание не задано.";
            if (IsModifierKey(h.Key)) return "Нажмите обычную клавишу вместе с Ctrl, Alt или Shift.";
            Mods m = h.Modifiers;
            if (m == (Mods.Win | Mods.Shift) && h.Key == Keys.S)
                return "Win+Shift+S — системное сочетание «Ножниц» Windows. Выберите другое.";
            if ((m & Mods.Win) != 0)
                return "Сочетания с клавишей Win зарезервированы Windows. Используйте Ctrl, Alt или Shift.";
            if (m == Mods.Alt && h.Key == Keys.F4) return "Alt+F4 закрывает окна — выберите другое сочетание.";
            if (m == (Mods.Ctrl | Mods.Alt) && h.Key == Keys.Delete) return "Ctrl+Alt+Delete — системное сочетание.";
            if (h.Key == Keys.Escape || h.Key == Keys.Tab || h.Key == Keys.Enter)
                return "Esc, Tab и Enter нужны для работы с окнами — выберите другую клавишу.";
            if (m == Mods.None && !IsStandaloneKey(h.Key))
                return "Добавьте Ctrl, Alt или Shift — одиночная клавиша мешала бы печатать.";
            if (m == Mods.Shift && !IsStandaloneKey(h.Key))
                return "Shift с буквой или цифрой мешал бы печатать заглавные буквы. Добавьте Ctrl или Alt.";
            if (m == Mods.Ctrl && "CVXZYASPFNOWTR".IndexOf(KeyName(h.Key), StringComparison.Ordinal) >= 0 && KeyName(h.Key).Length == 1)
                return "Ctrl+" + KeyName(h.Key) + " — стандартное сочетание (копировать, вставить, сохранить…). Добавьте Shift или Alt.";
            return null;
        }

        /// <summary>Первое свободное сочетание из списка (кроме исключённых).</summary>
        public static Hotkey? FindFree(IEnumerable<Hotkey> candidates, Func<Hotkey, bool> canRegister, params Hotkey[] exclude)
        {
            foreach (var c in candidates)
            {
                if (exclude != null && exclude.Contains(c)) continue;
                if (Validate(c) != null) continue;
                bool ok;
                try { ok = canRegister(c); } catch { ok = false; }
                if (ok) return c;
            }
            return null;
        }

        public uint NativeModifiers
        {
            get
            {
                uint r = 0;
                if ((Modifiers & Mods.Alt) != 0) r |= Native.MOD_ALT;
                if ((Modifiers & Mods.Ctrl) != 0) r |= Native.MOD_CONTROL;
                if ((Modifiers & Mods.Shift) != 0) r |= Native.MOD_SHIFT;
                if ((Modifiers & Mods.Win) != 0) r |= Native.MOD_WIN;
                return r;
            }
        }

        public bool Equals(Hotkey other) { return Modifiers == other.Modifiers && Key == other.Key; }
        public override bool Equals(object obj) { return obj is Hotkey && Equals((Hotkey)obj); }
        public override int GetHashCode() { return ((int)Modifiers << 16) ^ (int)Key; }
        public static bool operator ==(Hotkey a, Hotkey b) { return a.Equals(b); }
        public static bool operator !=(Hotkey a, Hotkey b) { return !a.Equals(b); }
    }

    /// <summary>Глобальные горячие клавиши через RegisterHotKey.</summary>
    internal sealed class HotkeyManager : NativeWindow, IDisposable
    {
        public const int CaptureId = 1, UndoId = 2, ProbeId = 99;
        private readonly HashSet<int> registered = new HashSet<int>();
        public event Action<int> Pressed;

        public HotkeyManager() { CreateHandle(new CreateParams()); }

        public bool Register(int id, Hotkey h)
        {
            Unregister(id);
            if (h.IsEmpty) return false;
            bool ok = Native.RegisterHotKey(Handle, id, h.NativeModifiers | Native.MOD_NOREPEAT, (uint)h.Key);
            if (ok) registered.Add(id);
            return ok;
        }

        public void Unregister(int id)
        {
            if (registered.Remove(id)) Native.UnregisterHotKey(Handle, id);
        }

        public void UnregisterAll() { foreach (var id in registered.ToArray()) Unregister(id); }

        /// <summary>Можно ли зарегистрировать сочетание (регистрирует и сразу снимает).</summary>
        public bool CanRegister(Hotkey h)
        {
            if (!Register(ProbeId, h)) return false;
            Unregister(ProbeId);
            return true;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                var handler = Pressed;
                if (handler != null) handler(m.WParam.ToInt32());
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose() { UnregisterAll(); DestroyHandle(); }
    }
}
