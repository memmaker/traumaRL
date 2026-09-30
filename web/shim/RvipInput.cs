/* RVIP: one key queue for both of TraumaRL's input paths (SdlDotNet key-up events in
   the main loop, libtcod Keyboard.WaitForKeyPress in modal screens). Keys come from the
   backend as "code\tkey\tmods" (browser KeyboardEvent.code / .key, mods: s c a). */
using System;
using SdlDotNet.Input;
using libtcodWrapper;
namespace RogueBasin{
	public interface IRvipBackend{
		string WaitKey(int ms); //ms < 0: block; "" on timeout
		void Sleep(int ms);
		void Present(int[] cells, string info);
		void Quit();
		void FileChanged(string name); //a persistent file was written or deleted (web: mirror to IndexedDB)
		void Sound(string name); //RVIP 6: a game action names its sound effect (the page plays it if Sound is on)
		void Beacon(string query); //RVIP 9: report a finished run (graveyard/leaderboard); the page sends it through the outbox
	}
	[System.Serializable] public class RvipKey{
		public string Code, KeyName; public bool Shift, Ctrl, Alt;
		public char Char => KeyName.Length == 1 ? KeyName[0] : '\0';
		static Key SdlKey(string code){
			if(code.StartsWith("Key") && code.Length == 4) return Key.A + (code[3] - 'A');
			if(code.StartsWith("Digit")) return Key.Zero + (code[5] - '0');
			if(code.StartsWith("Numpad") && code.Length == 7 && char.IsDigit(code[6])) return Key.Keypad0 + (code[6] - '0');
			if(code.Length >= 2 && code[0] == 'F' && int.TryParse(code.Substring(1), out int f) && f >= 1 && f <= 12) return Key.F1 + (f - 1);
			switch(code){
				case "ArrowUp": return Key.UpArrow; case "ArrowDown": return Key.DownArrow;
				case "ArrowLeft": return Key.LeftArrow; case "ArrowRight": return Key.RightArrow;
				case "Enter": return Key.Return; case "NumpadEnter": return Key.KeypadEnter;
				case "Escape": return Key.Escape; case "Space": return Key.Space; case "Backspace": return Key.Backspace;
				case "Tab": return Key.Tab; case "Period": return Key.Period; case "Comma": return Key.Comma;
				case "Slash": return Key.Slash; case "Minus": return Key.Minus; case "Equal": return Key.Equals;
				case "Semicolon": return Key.Semicolon; case "Quote": return Key.Quote; case "Backslash": return Key.Backslash;
				case "BracketLeft": return Key.LeftBracket; case "BracketRight": return Key.RightBracket; case "Backquote": return Key.BackQuote;
				case "NumpadDecimal": return Key.KeypadPeriod; case "NumpadAdd": return Key.KeypadPlus; case "NumpadSubtract": return Key.KeypadMinus;
				case "NumpadMultiply": return Key.KeypadMultiply; case "NumpadDivide": return Key.KeypadDivide;
				case "Home": return Key.Home; case "End": return Key.End; case "PageUp": return Key.PageUp; case "PageDown": return Key.PageDown;
				case "Insert": return Key.Insert; case "Delete": return Key.Delete;
			}
			return Key.Unknown;
		}
		public KeyboardEventArgs ToSdl(){
			ModifierKeys m = ModifierKeys.None;
			if(Shift) m |= ModifierKeys.LeftShift; if(Ctrl) m |= ModifierKeys.LeftControl; if(Alt) m |= ModifierKeys.LeftAlt;
			return new KeyboardEventArgs(SdlKey(Code), m, Char.ToString(), false);
		}
		public KeyPress ToTcod(){
			KeyCode k = KeyCode.TCODK_CHAR;
			if(Code.StartsWith("Numpad") && Code.Length == 7 && char.IsDigit(Code[6])) k = KeyCode.TCODK_KP0 + (Code[6] - '0');
			else if(Code.StartsWith("Digit") && !Shift) k = KeyCode.TCODK_0 + (Code[5] - '0');
			else switch(Code){
				case "ArrowUp": k = KeyCode.TCODK_UP; break; case "ArrowDown": k = KeyCode.TCODK_DOWN; break;
				case "ArrowLeft": k = KeyCode.TCODK_LEFT; break; case "ArrowRight": k = KeyCode.TCODK_RIGHT; break;
				case "Enter": k = KeyCode.TCODK_ENTER; break; case "NumpadEnter": k = KeyCode.TCODK_KPENTER; break;
				case "Escape": k = KeyCode.TCODK_ESCAPE; break; case "Space": k = KeyCode.TCODK_SPACE; break;
				case "Backspace": k = KeyCode.TCODK_BACKSPACE; break; case "Tab": k = KeyCode.TCODK_TAB; break;
				case "PageUp": k = KeyCode.TCODK_PAGEUP; break; case "PageDown": k = KeyCode.TCODK_PAGEDOWN; break;
				case "Home": k = KeyCode.TCODK_HOME; break; case "End": k = KeyCode.TCODK_END; break;
				case "Delete": k = KeyCode.TCODK_DELETE; break; case "Insert": k = KeyCode.TCODK_INSERT; break;
				case "NumpadDecimal": k = KeyCode.TCODK_KPDEC; break; case "NumpadAdd": k = KeyCode.TCODK_KPADD; break;
				case "NumpadSubtract": k = KeyCode.TCODK_KPSUB; break;
				default: if(Char == '\0') k = KeyCode.TCODK_NONE; break;
			}
			byte c = k == KeyCode.TCODK_CHAR || (k >= KeyCode.TCODK_0 && k <= KeyCode.TCODK_9) || k == KeyCode.TCODK_SPACE ? (byte)Char : (byte)0;
			return new KeyPress(k, c, Shift, Ctrl, Alt);
		}
	}
	public static class RvipInput{
		public static IRvipBackend Backend;
		public static bool InMainLoop; //Events.Run is waiting (not a modal screen)
		public static int IdleWait = 40; //ms the event loop waits for a key between ticks
		public static RvipKey NextKey(int ms){
			while(true){
				string s = Backend.WaitKey(ms);
				if(string.IsNullOrEmpty(s)){ if(ms >= 0) return null; continue; }
				string[] p = s.Split('\t');
				if(p.Length < 3) continue;
				if(p[0] == "RvipSave"){ if(InMainLoop) RvipSave.Request(); continue; } //the page asks (tab hidden / closed)
				var k = new RvipKey{ Code = p[0], KeyName = p[1], Shift = p[2].Contains('s'), Ctrl = p[2].Contains('c'), Alt = p[2].Contains('a') };
				if(k.Code == "ShiftLeft" || k.Code == "ShiftRight" || k.Code.StartsWith("Control") || k.Code.StartsWith("Alt") || k.Code.StartsWith("Meta")) continue;
				return k;
			}
		}
		public static void Sleep(int ms){ Backend.Sleep(ms); }
		public static void Sound(string name){ if(Backend != null) Backend.Sound(name); }
		public static string PlayerName = ""; //RVIP 9: asked once by the page (the game itself always says "Dave")
		public static string Killer; //RVIP 9: SingleDescription of the last monster that hurt the player (Monster.AttackPlayer); null = own/environment damage
		//RVIP 9: one report per finished run, from Dungeon.EndOfGame (death, escape-pod win, quit). Score = the end screen's kill points.
		public static void Beacon(bool won, bool quit){
			try{
				var d = Game.Dungeon; var p = d.Player;
				string ev = won ? "win" : quit ? "quit" : "death";
				string q = "g=traumarl&ev=" + ev;
				if(PlayerName != "") q += "&name=" + Uri.EscapeDataString(PlayerName);
				if(ev == "death" && !string.IsNullOrEmpty(Killer)){
					string k = Killer.Trim();
					foreach(string a in new[]{"a ","an ","the "}) if(k.ToLowerInvariant().StartsWith(a)){ k = k.Substring(a.Length); break; }
					q += "&killer=" + Uri.EscapeDataString(k);
				}
				q += "&depth=" + (p.LocationLevel + 1) + "&score=" + d.GetKillRecord().killScore + "&turns=" + p.TurnCount;
				if(Backend != null) Backend.Beacon(q);
			}
			catch(Exception){}
			Killer = null;
		}
	}
}
