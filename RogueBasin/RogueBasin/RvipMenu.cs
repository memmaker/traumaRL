// RVIP stage 3: Enter command menu and item menus (weapons + wetware = TraumaRL's inventory).
// Entries are built from the game's own tables (ItemMapping, the player's inventory items,
// IEquippableItem.Has*Action) and run by handing the command's normal key to ProcessKeypress,
// so every command keeps its own checks. The open menu is published as JSON (RvipMenuJson)
// for the renderer; the page only draws it.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SdlDotNet.Input;

namespace RogueBasin
{
    public partial class RogueBase
    {
        class RvipEntry
        {
            public string Label, KeyName; public int Rgb = 0xC0C0C0; public bool Header;
            public KeyboardEventArgs Run;            // command key to run, or
            public Func<RvipMenu> Sub;               // a sub-menu (item list, item actions)
            public RvipEntry Main;                   // item list: the item's main action
        }
        class RvipMenu
        {
            public string Title; public List<RvipEntry> Rows = new List<RvipEntry>(); public int Cur;
            public RvipMenu Parent; public bool ItemList;
        }

        RvipMenu rvipMenu;
        bool rvipReopenItems;
        /// Published for the renderer: null when no menu is open.
        public static string RvipMenuJson { get; private set; }

        static KeyboardEventArgs RvipKey(Key k, string ch, bool shift = false)
        {
            return new KeyboardEventArgs(k, shift ? ModifierKeys.LeftShift : ModifierKeys.None, ch, false);
        }

        static bool RvipLockDebug
        {
            get { return Game.Config.DebugMode || (Game.Config.Entries.ContainsKey("rviplocks") && Game.Config.Entries["rviplocks"] == "true"); }
        }

        static RvipEntry RvipHeader(string s) { return new RvipEntry { Label = s, Header = true, Rgb = 0xFFD060 }; }

        RvipMenu RvipCommandMenu()
        {
            var m = new RvipMenu { Title = "Commands" };
            var w = Game.Dungeon.Player.GetEquippedWeapon();
            m.Rows.Add(RvipHeader("Actions"));
            string fire = w == null ? null : w.HasFireAction() ? "Fire weapon" : w.HasThrowAction() ? "Throw weapon" : w.HasOperateAction() ? "Use weapon" : null;
            if (fire != null) m.Rows.Add(new RvipEntry { Label = fire, KeyName = "f", Run = RvipKey(Key.F, "f") });
            m.Rows.Add(new RvipEntry { Label = "Examine", KeyName = "x", Run = RvipKey(Key.X, "x") });
            m.Rows.Add(new RvipEntry { Label = "Wait a turn", KeyName = ".", Run = RvipKey(Key.Period, ".") });
            m.Rows.Add(RvipHeader("Items"));
            m.Rows.Add(new RvipEntry { Label = "Weapons and wetware", KeyName = "i", Sub = () => RvipItemMenu() });
            m.Rows.Add(RvipHeader("Travel"));
            m.Rows.Add(new RvipEntry { Label = "Auto-explore", KeyName = "e", Run = RvipKey(Key.E, "e") });
            m.Rows.Add(new RvipEntry { Label = "Walk to elevator up", KeyName = "<", Run = RvipKey(Key.LessThan, "<", true) });
            m.Rows.Add(new RvipEntry { Label = "Walk to elevator down", KeyName = ">", Run = RvipKey(Key.GreaterThan, ">", true) });
            m.Rows.Add(RvipHeader("Other"));
            m.Rows.Add(new RvipEntry { Label = "Help", KeyName = "?", Run = RvipKey(Key.Slash, "?", true) });
            if (RvipLockDebug)
            {
                m.Rows.Add(RvipHeader("Debug"));
                m.Rows.Add(new RvipEntry { Label = "Toggle all locks open", KeyName = "K", Run = RvipKey(Key.K, "K", true) });
            }
            return m;
        }

        /// TraumaRL's inventory: weapons (number keys) and wetware (letter keys) the player holds.
        RvipMenu RvipItemMenu()
        {
            var p = Game.Dungeon.Player;
            var m = new RvipMenu { Title = "Weapons and wetware", ItemList = true };
            var eqW = p.GetEquippedWeapon() as Item; var eqWare = p.GetEquippedWetware() as Item;
            var weapons = new List<RvipEntry>();
            foreach (var kv in ItemMapping.WeaponMapping)
            {
                var t = p.HeavyWeaponTranslation(kv.Value);
                if (!p.IsInventoryTypeAvailable(t)) continue;
                var it = p.Inventory.GetItemsOfType(t).First();
                bool eq = eqW != null && eqW.GetType() == t;
                var acts = RvipWeaponActions(it as IEquippableItem, kv.Key, eq);
                weapons.Add(RvipItemEntry(it, eq ? " (in hand)" : "", kv.Key.ToString(), acts));
            }
            var ware = new List<RvipEntry>();
            foreach (var kv in ItemMapping.WetwareMapping)
            {
                if (!p.IsWetwareTypeAvailable(kv.Value)) continue;
                var it = p.Inventory.GetItemsOfType(kv.Value).First();
                bool on = eqWare != null && eqWare.GetType() == kv.Value, off = p.IsWetwareTypeDisabled(kv.Value);
                var acts = new List<RvipEntry>();
                if (!off) acts.Add(new RvipEntry { Label = on ? "Switch off" : "Switch on", KeyName = kv.Key.ToString(), Run = RvipKey(Key.A + (kv.Key - 'a'), kv.Key.ToString()) });
                ware.Add(RvipItemEntry(it, on ? " (on)" : off ? " (disabled)" : "", kv.Key.ToString(), acts));
            }
            if (weapons.Count > 0) { m.Rows.Add(RvipHeader("Weapons")); m.Rows.AddRange(weapons); }
            if (ware.Count > 0) { m.Rows.Add(RvipHeader("Wetware")); m.Rows.AddRange(ware); }
            if (m.Rows.Count == 0) m.Rows.Add(RvipHeader("(nothing)"));
            return m;
        }

        List<RvipEntry> RvipWeaponActions(IEquippableItem w, int num, bool equipped)
        {
            var acts = new List<RvipEntry>();
            if (!equipped) acts.Add(new RvipEntry { Label = "Equip", KeyName = num.ToString(), Run = RvipKey(Key.Zero + num, num.ToString()) });
            else if (w != null)
            {
                if (w.HasFireAction()) acts.Add(new RvipEntry { Label = "Fire", KeyName = "f", Run = RvipKey(Key.F, "f") });
                else if (w.HasThrowAction()) acts.Add(new RvipEntry { Label = "Throw", KeyName = "f", Run = RvipKey(Key.F, "f") });
                else if (w.HasOperateAction()) acts.Add(new RvipEntry { Label = "Use", KeyName = "f", Run = RvipKey(Key.F, "f") });
            }
            return acts;
        }

        RvipEntry RvipItemEntry(Item it, string state, string key, List<RvipEntry> acts)
        {
            var c = it.GetColour();
            var e = new RvipEntry { Label = it.SingleItemDescription + state, KeyName = key, Rgb = (c.R << 16) | (c.G << 8) | c.B, Main = acts.FirstOrDefault() };
            string title = it.SingleItemDescription;
            e.Sub = () => { var s = new RvipMenu { Title = title }; if (acts.Count == 0) s.Rows.Add(RvipHeader("(no action now)")); s.Rows.AddRange(acts); return s; };
            return e;
        }

        void RvipOpen(RvipMenu m, RvipMenu parent)
        {
            m.Parent = parent; rvipMenu = m;
            m.Cur = m.Rows.FindIndex(r => !r.Header); if (m.Cur < 0) m.Cur = 0;
            RvipPublish();
        }
        void RvipClose() { rvipMenu = null; RvipPublish(); }

        void RvipPublish()
        {
            Screen.Instance.NeedsUpdate = true;
            if (rvipMenu == null) { RvipMenuJson = null; return; }
            var sb = new StringBuilder("{\"title\":").Append(RvipJson(rvipMenu.Title)).Append(",\"cur\":").Append(rvipMenu.Cur).Append(",\"rows\":[");
            int n = 0;
            for (int i = 0; i < rvipMenu.Rows.Count; i++)
            {
                var r = rvipMenu.Rows[i];
                if (i > 0) sb.Append(',');
                string acc = r.Header ? "" : ((char)('a' + n++)).ToString();
                sb.Append('[').Append(RvipJson(acc)).Append(',').Append(RvipJson(r.Label)).Append(',').Append(RvipJson(r.KeyName ?? "")).Append(',').Append(r.Rgb).Append(',').Append(r.Header ? 1 : 0).Append(']');
            }
            RvipMenuJson = sb.Append("]}").ToString();
        }
        /// RVIP stage 5: the web Inventory window, same rows as the i list: [label, key, rgb, header]
        public string RvipInventoryJson()
        {
            if (Game.Dungeon == null || Game.Dungeon.Player == null) return "[]";
            var sb = new StringBuilder("[");
            var m = RvipItemMenu();
            for (int i = 0; i < m.Rows.Count; i++)
            {
                var r = m.Rows[i];
                if (i > 0) sb.Append(',');
                sb.Append('[').Append(RvipJson(r.Label)).Append(',').Append(RvipJson(r.KeyName ?? "")).Append(',').Append(r.Rgb).Append(',').Append(r.Header ? 1 : 0).Append(']');
            }
            return sb.Append(']').ToString();
        }
        /// RVIP stage 5: the game waits for a map command (no menu, no targetting, no movie)
        public bool RvipAtCmd { get { return inputState == InputState.MapMovement && rvipMenu == null; } }

        static string RvipJson(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s) { if (c == '"' || c == '\\') sb.Append('\\').Append(c); else if (c < 32) sb.Append(' '); else sb.Append(c); }
            return sb.Append('"').ToString();
        }

        /// Key hook (before ProcessKeypress). Returns true when the key was consumed. When a
        /// menu entry runs a command, args is replaced by that command's key and false returned.
        bool RvipMenuKey(ref KeyboardEventArgs args)
        {
            if (rvipMenu == null)
            {
                if (args.Down || inputState != InputState.MapMovement) return false;
                if (args.Key == Key.Return || args.Key == Key.KeypadEnter) { RvipOpen(RvipCommandMenu(), null); return true; }
                if (args.KeyboardCharacter == "i" && args.Mod == ModifierKeys.None) { RvipOpen(RvipItemMenu(), null); return true; }
                return false;
            }
            if (args.Down) return true;
            var m = rvipMenu; bool shift = (args.Mod & ModifierKeys.ShiftKeys) != 0;
            RvipEntry pick = null; bool main = false;
            string ch = args.KeyboardCharacter ?? "";
            // accelerators first (letters), then cursor keys
            if (ch.Length == 1 && char.IsLetter(ch[0]) && (args.Mod & ModifierKeys.ControlKeys) == 0)
            {
                int idx = char.ToLower(ch[0]) - 'a', n = 0;
                foreach (var r in m.Rows) { if (r.Header) continue; if (n++ == idx) { pick = r; break; } }
                if (pick == null) return true;
                main = m.ItemList && !shift;
            }
            else switch (args.Key)
            {
                case Key.Escape: case Key.Keypad0: case Key.KeypadPeriod: case Key.Period: RvipClose(); return true;
                case Key.UpArrow: case Key.Keypad8: RvipMove(-1); return true;
                case Key.DownArrow: case Key.Keypad2: RvipMove(1); return true;
                case Key.LeftArrow: case Key.Keypad4: if (m.Parent != null) { rvipMenu = m.Parent; RvipPublish(); } else RvipClose(); return true;
                case Key.KeypadPlus: if (!m.ItemList) return true; pick = m.Rows[m.Cur]; main = true; break;
                case Key.RightArrow: case Key.Keypad6: case Key.Return: case Key.KeypadEnter: case Key.Space: case Key.Keypad5:
                    pick = m.Rows[m.Cur]; break;
                default: return true;
            }
            if (pick == null || pick.Header) return true;
            if (main) { if (pick.Main == null) return true; pick = pick.Main; }
            if (pick.Sub != null) { var parent = m; RvipOpen(pick.Sub(), parent); return true; }
            if (pick.Run == null) return true;
            // remember whether this came from the item list, to reopen it after the action
            bool fromItems = m.ItemList || (m.Parent != null && m.Parent.ItemList);
            RvipClose();
            rvipReopenItems = fromItems;
            args = pick.Run;
            return false;
        }

        void RvipMove(int d)
        {
            var m = rvipMenu; int i = m.Cur;
            for (int k = 0; k < m.Rows.Count; k++) { i = (i + d + m.Rows.Count) % m.Rows.Count; if (!m.Rows[i].Header) break; }
            m.Cur = i; RvipPublish();
        }

        /// After a command run from the item list: reopen it when the game is back at the map and no monster is in view.
        void RvipAfterCommand()
        {
            if (!rvipReopenItems) return;
            rvipReopenItems = false;
            if (inputState == InputState.MapMovement && autoMode == AutoMode.None && AutoVisibleMonsters().Count == 0)
                RvipOpen(RvipItemMenu(), null);
        }
    }
}
