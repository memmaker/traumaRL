/* RVIP: the SdlDotNet surface TraumaRL uses (event loop, key events), without SDL.
   Events.Run() ticks the game and feeds it keys from RvipInput; drawing goes through
   MapRendererSDLDotNet (web/shim/WebRenderer.cs). */
using System;
namespace SdlDotNet.Core{
	[System.Serializable] public class QuitEventArgs : EventArgs{}
	public class TickEventArgs : EventArgs{
		public TickEventArgs(int tick, int elapsed){ Tick = tick; TicksElapsed = elapsed; }
		public int Tick{ get; } public int TicksElapsed{ get; } public int Fps => 30;
	}
	public static class Events{
		public static event EventHandler<QuitEventArgs> Quit;
		public static event EventHandler<TickEventArgs> Tick;
		public static event EventHandler<SdlDotNet.Input.KeyboardEventArgs> KeyboardUp;
		public static event EventHandler<SdlDotNet.Input.KeyboardEventArgs> KeyboardDown;
		static bool running;
		/// Loop: one tick (the game advances and redraws), then deliver a key or wait up to one frame for one.
		public static void Run(){
			running = true;
			var clock = System.Diagnostics.Stopwatch.StartNew();
			long last = 0;
			while(running){
				long now = clock.ElapsedMilliseconds;
				Tick?.Invoke(null, new TickEventArgs((int)now, (int)(now - last)));
				last = now;
				if(!running) break;
				RogueBasin.RvipInput.InMainLoop = true;
				var k = RogueBasin.RvipInput.NextKey(RogueBasin.RvipInput.IdleWait);
				RogueBasin.RvipInput.InMainLoop = false;
				if(k != null){
					var a = k.ToSdl();
					KeyboardDown?.Invoke(null, new SdlDotNet.Input.KeyboardEventArgs(a.Key, a.Mod, a.KeyboardCharacter, true));
					KeyboardUp?.Invoke(null, a);
				}
			}
		}
		public static void QuitApplication(){ running = false; }
	}
}
namespace SdlDotNet.Graphics{ public class Surface{} }
namespace SdlDotNet.Input{
	[Flags] public enum ModifierKeys{ None = 0, LeftShift = 1, RightShift = 2, LeftControl = 0x40, RightControl = 0x80, LeftAlt = 0x100, RightAlt = 0x200, ShiftKeys = 3, ControlKeys = 0xC0, AltKeys = 0x300 }
	public enum Key{
		Unknown, Backspace, Tab, Clear, Return, Pause, Escape, Space, Exclamation, DoubleQuote, Hash, Dollar, Ampersand, Quote,
		LeftParenthesis, RightParenthesis, Asterisk, Plus, Comma, Minus, Period, Slash,
		Zero, One, Two, Three, Four, Five, Six, Seven, Eight, Nine, Colon, Semicolon, LessThan, Equals, GreaterThan, Question, At,
		LeftBracket, Backslash, RightBracket, Caret, Underscore, BackQuote,
		A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z, Delete,
		Keypad0, Keypad1, Keypad2, Keypad3, Keypad4, Keypad5, Keypad6, Keypad7, Keypad8, Keypad9,
		KeypadPeriod, KeypadDivide, KeypadMultiply, KeypadMinus, KeypadPlus, KeypadEnter, KeypadEquals,
		UpArrow, DownArrow, RightArrow, LeftArrow, Insert, Home, End, PageUp, PageDown,
		F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
		NumLock, CapsLock, ScrollLock, RightShift, LeftShift, RightControl, LeftControl, RightAlt, LeftAlt
	}
	[System.Serializable] public class KeyboardEventArgs : EventArgs{
		public KeyboardEventArgs(Key key, ModifierKeys mod, string ch, bool down){ Key = key; Mod = mod; KeyboardCharacter = ch; Down = down; }
		public Key Key{ get; } public ModifierKeys Mod{ get; } public string KeyboardCharacter{ get; } public bool Down{ get; }
	}
}
namespace System.Windows.Forms{
	public enum DialogResult{ None, OK, Cancel }
	public static class MessageBox{
		public static DialogResult Show(string text){ Console.WriteLine("MessageBox: " + text); RogueBasin.LogFile.Log.LogEntryDebug("MessageBox: " + text, RogueBasin.LogDebugLevel.High); return DialogResult.OK; }
		public static DialogResult Show(string text, string caption){ return Show(caption + ": " + text); }
	}
}
namespace RogueBasin{
	/// Stand-in for the WinForms graph viewer (debug tool): does nothing.
	[System.Serializable] public class ImageDisplay{
		public string Text{ get; set; }
		public void AssignImage(string f){}
		public void Show(){}
	}
}
