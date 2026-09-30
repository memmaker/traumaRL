/* RVIP ASan substitute: runs TraumaRL natively, headless.
   usage: TraumaNative [seed] [randomkeys]   (env SCRIPT = keys first: chars, ~ Enter, ` Escape, 8246 etc. are
   digits, _ no key for one tick; NOMON=1 removes monsters, MSGS=1 prints new messages; DUMP=1 prints the last screen; TRACE=1 prints each key). Keys come immediately, so every
   tick of the event loop gets one. Exit 0 = keys used up, 2 = crash. */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace RogueBasin{
	class Headless : IRvipBackend{
		Random rng; int left; Queue<string> script = new Queue<string>();
		public int[] last; public string lastInfo = ""; public int presents;
		static readonly string pool = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789<>.,;:?/!@#$%^&*()-=+[]";
		static readonly string[] specials = {"ArrowUp","ArrowDown","ArrowLeft","ArrowRight","Numpad1","Numpad2","Numpad3","Numpad4","Numpad5","Numpad6","Numpad7","Numpad8","Numpad9","Enter","Escape","Space"};
		public Headless(int seed, int keys){
			rng = new Random(seed); left = keys;
			string sc = Environment.GetEnvironmentVariable("SCRIPT");
			if(sc != null) foreach(char c in sc) script.Enqueue(K(c));
		}
		static string K(char c){
			if(c == '_') return "_";
			if(c == '~') return "Enter\tEnter\t";
			if(c == '`') return "Escape\tEscape\t";
			string code = char.IsLetter(c) ? "Key" + char.ToUpper(c) : char.IsDigit(c) ? "Digit" + c : c == '.' ? "Period" : c == ',' ? "Comma" : c == '/' ? "Slash" : c == ' ' ? "Space" : "";
			return code + "\t" + c + "\t" + (char.IsUpper(c) || "<>?:!@#$%^&*()+".IndexOf(c) >= 0 ? "s" : "");
		}
		public string WaitKey(int ms){
			string k;
			if(script.Count > 0){ k = script.Dequeue(); if(k == "_") return ""; } // '_' = no key this tick (lets explore run)
			else if(left-- > 0) k = rng.Next(4) == 0 ? specials[rng.Next(specials.Length)] + "\t\t" : K(pool[rng.Next(pool.Length)]);
			else{ Finish(0); return ""; }
			if(Environment.GetEnvironmentVariable("TRACE") != null) Console.WriteLine("KEY " + k.Replace('\t', ' '));
			return k;
		}
		public void Sleep(int ms){}
		string lastMsg = "";
		public void Present(int[] cells, string info){
			if(Environment.GetEnvironmentVariable("NOMON") != null && Game.Dungeon?.Monsters != null) Game.Dungeon.Monsters.Clear(); // explore test: no monster stops
			if(Environment.GetEnvironmentVariable("MSGS") != null){ int i = info.IndexOf("[2,1,"); string m = i < 0 ? "" : info.Substring(i); if(m != lastMsg && m != ""){ Console.WriteLine("MSG " + (Game.Dungeon?.Player?.LocationMap) + " " + m); } lastMsg = m; }
			last = (int[])cells.Clone(); lastInfo = info; presents++; }
		public void Quit(){ Finish(0); }
		public void Finish(int rc){
			Console.WriteLine("presents " + presents + " rc " + rc + (Game.Dungeon != null && Game.Dungeon.Player != null ? " level " + Game.Dungeon.Player.LocationLevel + " at " + Game.Dungeon.Player.LocationMap + " hp " + Game.Dungeon.Player.Hitpoints : ""));
			if(Environment.GetEnvironmentVariable("DUMP") != null) Console.WriteLine(Dump());
			if(Environment.GetEnvironmentVariable("NOMON") != null && Game.Dungeon?.Player != null){ var m = Game.Dungeon.Levels[Game.Dungeon.Player.LocationLevel]; int w = 0, sn = 0, lk = 0; for(int x = 0; x < m.width; x++) for(int y = 0; y < m.height; y++){ var q = m.mapSquares[x, y]; if(q.Walkable){ w++; if(q.SeenByPlayer) sn++; } if(q.Terrain == MapTerrain.ClosedLock) lk++; } Console.WriteLine("walkable seen " + sn + "/" + w + " locks " + lk + " elevators " + string.Join(",", Game.Dungeon.Features.OfType<Features.Elevator>().Where(e => e.LocationLevel == Game.Dungeon.Player.LocationLevel).Select(e => e.LocationMap + "->" + e.DestLevel + " seen " + m.mapSquares[e.LocationMap.x, e.LocationMap.y].SeenByPlayer))); }
			Environment.Exit(rc);
		}
		/// Top sprite per cell as a character (sprite ids below 128 are ASCII glyphs in TraumaSprites.png), then text runs.
		public string Dump(){
			if(last == null) return "(nothing presented)";
			const int C = MapRendererSDLDotNet.Cols, R = MapRendererSDLDotNet.Rows, S = MapRendererSDLDotNet.CELL;
			var g = new char[R][];
			for(int y = 0; y < R; y++){ g[y] = new char[C]; for(int x = 0; x < C; x++){ int o = (x + y*C)*S, n = last[o]; int id = n > 0 ? last[o + 1 + (n-1)*3] : 32; g[y][x] = id > 32 && id < 127 ? (char)id : n > 0 ? '#' : ' '; } }
			var sb = new System.Text.StringBuilder();
			foreach(var row in g) sb.Append(new string(row).TrimEnd()).Append('\n');
			return sb.Append(lastInfo).ToString();
		}
	}
	static class HeadlessMain{
		static int Main(string[] args){
			int seed = args.Length > 0 ? int.Parse(args[0]) : 1, keys = args.Length > 1 ? int.Parse(args[1]) : 300;
			var h = new Headless(seed, keys);
			RvipInput.Backend = h;
			var exc = new Dictionary<string,int>(); int nexc = 0;
			if(Environment.GetEnvironmentVariable("EXC") != null) AppDomain.CurrentDomain.FirstChanceException += (o, e) => { nexc++; string k = e.Exception.GetType().Name + ": " + e.Exception.Message; exc[k] = exc.GetValueOrDefault(k) + 1; };
			AppDomain.CurrentDomain.ProcessExit += (o, e) => { if(nexc > 0){ Console.WriteLine("exceptions " + nexc); foreach(var kv in exc) if(kv.Value > 5) Console.WriteLine("  " + kv.Value + " " + kv.Key); } };
			File.Copy(Path.Combine(AppContext.BaseDirectory, "config.txt"), "config.txt", true);
			try{ TraumaRL.RvipEntry.Run(); }
			catch(Exception e){ Console.WriteLine("UNHANDLED: " + e); h.Finish(2); }
			h.Finish(0);
			return 0;
		}
	}
}
namespace TraumaRL{
	public static class RvipEntry{ public static void Run(){ new TraumaRunner().TemplatedMapTest(); } }
}
