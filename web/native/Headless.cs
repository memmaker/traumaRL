/* RVIP ASan substitute: runs TraumaRL natively, headless.
   usage: TraumaNative [seed] [randomkeys]   (env SCRIPT = keys first: chars, ~ Enter, ` Escape, } { arrow down/up, 8246 etc. are
   digits, _ no key for one tick; NOMON=1 removes monsters, MSGS=1 prints new messages; DUMP=1 prints the last screen; TRACE=1 prints each key). Keys come immediately, so every
   tick of the event loop gets one. Exit 0 = keys used up, 2 = crash. */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace RogueBasin{
	class Headless : IRvipBackend{
		Random rng; int left; int saveTries; bool savedNow; DateTime started = DateTime.UtcNow; Queue<string> script = new Queue<string>();
		public int[] last; public string lastInfo = ""; public int presents;
		static readonly string pool = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789<>.,;:?/!@#$%^&*()-=+[]";
		static readonly string Pool = Environment.GetEnvironmentVariable("NOQUIT") != null ? pool.Replace("Q", "") : pool; // NOQUIT=1: no Q (quit) key
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
			if(c == '}') return "ArrowDown\t\t"; if(c == '{') return "ArrowUp\t\t"; // RVIP menus
			if(c == '}') return "ArrowDown\t\t"; if(c == '{') return "ArrowUp\t\t"; // RVIP menus
			string code = char.IsLetter(c) ? "Key" + char.ToUpper(c) : char.IsDigit(c) ? "Digit" + c : c == '.' ? "Period" : c == ',' ? "Comma" : c == '/' ? "Slash" : c == ' ' ? "Space" : "";
			return code + "\t" + c + "\t" + (char.IsUpper(c) || "<>?:!@#$%^&*()+".IndexOf(c) >= 0 ? "s" : "");
		}
		public string WaitKey(int ms){
			string k;
			if(script.Count > 0){ k = script.Dequeue(); if(k == "_") return ""; } // '_' = no key this tick (lets explore run)
			else if(left-- > 0) k = rng.Next(4) == 0 ? specials[rng.Next(specials.Length)] + "\t\t" : K(Pool[rng.Next(Pool.Length)]);
			else if(Environment.GetEnvironmentVariable("SAVE") != null && saveTries++ < 400 && !savedNow){ // save test: get back to the map, then ask for a save
				if(saveTries % 2 == 1) return "Escape\t\t";
				if(saveTries < 6) Console.WriteLine("try " + Game.Base.RvipAtCmd + " " + Game.Base.RvipCanSave + " main " + RvipInput.InMainLoop); RvipSave.Request(); if(File.Exists(RvipSave.File) && new FileInfo(RvipSave.File).LastWriteTimeUtc > started){ savedNow = true;
					if(Environment.GetEnvironmentVariable("RT") != null){ var d = Game.Dungeon; Console.WriteLine("rt random " + RvipSave.RoundTrip(Game.Random)); Console.WriteLine("levels " + d.Levels.Count); FieldSurrogate.Stats = new Dictionary<string,int>(); var sw = System.Diagnostics.Stopwatch.StartNew(); RvipSave.RoundTrip(new object[]{ d, Game.MessageQueue, Game.Random }); Console.WriteLine("rt all ms " + sw.ElapsedMilliseconds); foreach(var kv in FieldSurrogate.Stats.OrderByDescending(x => x.Value).Take(8)) Console.WriteLine("surr " + kv.Value + " " + kv.Key); FieldSurrogate.Stats = null; foreach(var lf in typeof(Map).GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic)){ var lv = lf.GetValue(d.Levels[0]); if(lv != null) Console.WriteLine("rt L0." + lf.Name + " " + RvipSave.RoundTrip(lv)); } Console.WriteLine("rt mq " + RvipSave.RoundTrip(Game.MessageQueue)); foreach(var f in typeof(Dungeon).GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic)){ var v = f.GetValue(d); if(v != null){ var r = RvipSave.RoundTrip(v); Console.WriteLine("rt " + f.Name + " " + r); if(!r.StartsWith("ok")) Drill(v, f.Name, 0); } } }
					Console.WriteLine("SAVED after " + saveTries + " " + Fingerprint()); }
				return "";
			}
			else{ Finish(0); return ""; }
			if(Environment.GetEnvironmentVariable("TRACE") != null) Console.WriteLine("KEY " + k.Replace('\t', ' '));
			return k;
		}
		public void Sleep(int ms){}
		string lastMsg = "";
		public void Present(int[] cells, string info){
			if(Environment.GetEnvironmentVariable("NOMON") != null && Game.Dungeon?.Monsters != null) { Game.Dungeon.Monsters.Clear(); Game.Dungeon.AllLocksOpen = Environment.GetEnvironmentVariable("OPEN") != null; } // explore test: no monster stops
			if(Environment.GetEnvironmentVariable("MSGS") != null){ int i = info.IndexOf("[2,1,"); string m = i < 0 ? "" : info.Substring(i); if(m != lastMsg && m != ""){ Console.WriteLine("MSG " + (Game.Dungeon?.Player?.LocationMap) + " " + m); } lastMsg = m; }
			if(presents == 0 && Environment.GetEnvironmentVariable("LOADFP") != null) Console.WriteLine("LOADED " + Fingerprint());
			last = (int[])cells.Clone(); lastInfo = info; presents++;
			if(Environment.GetEnvironmentVariable("COVER") != null && Game.Dungeon?.Player != null) Cover(); }
		/// RVIP stage 4: sprite coverage. Sprite id = Representation (a char); ids < 256 are the sheet's CP437 glyph rows,
		/// >= 256 are pictures. Counts distinct kinds placed in the generated station, and every concrete Monster/Item/Feature
		/// type with a parameterless constructor.
		void Cover(){
			var placed = new SortedDictionary<string,int>();
			void Add(object o, int id){ placed[(o is MapObject mo ? mo.GetType().Namespace + "." : "") + o.GetType().Name] = id; }
			foreach(var m in Game.Dungeon.Monsters) Add(m, m.Representation);
			foreach(var i in Game.Dungeon.Items) Add(i, i.Representation);
			foreach(var f in Game.Dungeon.Features) Add(f, f.Representation);
			foreach(var kv in Game.Dungeon.Locks) foreach(var l in kv.Value) Add(l, l.Representation);
			var terr = new SortedDictionary<string,int>();
			foreach(var lv in Game.Dungeon.Levels) for(int x = 0; x < lv.width; x++) for(int y = 0; y < lv.height; y++){ var t = lv.mapSquares[x, y].Terrain; terr["terrain." + t] = StringEquivalent.TerrainChars[t]; }
			int Pic(IDictionary<string,int> d) => d.Values.Count(v => v >= 256);
			Console.WriteLine("placed kinds " + placed.Count + " picture " + Pic(placed));
			foreach(var kv in placed) Console.WriteLine("  " + (kv.Value >= 256 ? "pic " : "GLYPH ") + kv.Value + " " + kv.Key);
			Console.WriteLine("terrain kinds " + terr.Count + " picture " + Pic(terr));
			foreach(var kv in terr) Console.WriteLine("  " + (kv.Value >= 256 ? "pic " : "glyph ") + kv.Value + " " + kv.Key);
			var all = new SortedDictionary<string,int>(); int noctor = 0;
			foreach(var t in typeof(Monster).Assembly.GetTypes().Concat(typeof(TraumaRL.RvipEntry).Assembly.GetTypes()))
				if(!t.IsAbstract && (typeof(Monster).IsAssignableFrom(t) || typeof(Item).IsAssignableFrom(t) || typeof(Feature).IsAssignableFrom(t)))
					try{ var o = (MapObject)Activator.CreateInstance(t); all[t.Namespace + "." + t.Name] = o.Representation; } catch { noctor++; }
			Console.WriteLine("all types " + all.Count + " picture " + Pic(all) + " (no default ctor " + noctor + ")");
			foreach(var kv in all) if(kv.Value < 256) Console.WriteLine("  GLYPH " + kv.Value + " '" + (char)kv.Value + "' " + kv.Key);
			Finish(0);
		}
		public void Quit(){ Finish(0); }
		static void Drill(object o, string path, int depth){
			var fs = new List<System.Reflection.FieldInfo>();
			for(var t = o.GetType(); t != null; t = t.BaseType) fs.AddRange(t.GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.DeclaredOnly));
			bool any = false;
			foreach(var f in fs){ var v = f.GetValue(o); if(v == null || v.GetType().IsPrimitive || v is string) continue; var r = RvipSave.RoundTrip(v); if(!r.StartsWith("ok")){ any = true; Console.WriteLine("  bad " + path + "." + f.Name + " : " + v.GetType() + " " + r); if(depth < 6) Drill(v, path + "." + f.Name, depth + 1); } }
			if(!any) Console.WriteLine("  LEAF " + path + " " + o.GetType());
		}
		/// Same run? hero name/level/position/hp, turn, monster/item counts, hash of the current level's terrain + seen cells.
		public static string Fingerprint(){
			var d = Game.Dungeon; var p = d.Player; var m = d.Levels[p.LocationLevel]; int h = 17;
			for(int x = 0; x < m.width; x++) for(int y = 0; y < m.height; y++){ h = h * 31 + (int)m.mapSquares[x, y].Terrain; h = h * 3 + (m.mapSquares[x, y].SeenByPlayer ? 1 : 0); }
			return "fp " + p.Name + " L" + p.LocationLevel + " " + p.LocationMap + " hp" + p.Hitpoints + " mon" + d.Monsters.Count + " items" + d.Items.Count + " locks" + d.Locks.Count + " map" + h.ToString("x");
		}
		public void Sound(string name){ if(Environment.GetEnvironmentVariable("SOUNDS") != null) Console.WriteLine("SOUND " + name); }
		public void FileChanged(string name){ Console.WriteLine("FILE " + name + (File.Exists(name) ? " " + new FileInfo(name).Length : " deleted")); }
		public void Finish(int rc){
			if(Game.Dungeon != null && Game.Dungeon.Player != null) try{ Console.WriteLine(Fingerprint()); }catch{}
			Console.WriteLine("presents " + presents + " rc " + rc + (Game.Dungeon != null && Game.Dungeon.Player != null ? " level " + Game.Dungeon.Player.LocationLevel + " at " + Game.Dungeon.Player.LocationMap + " hp " + Game.Dungeon.Player.Hitpoints : ""));
			if(Environment.GetEnvironmentVariable("DUMP") != null) Console.WriteLine(Dump());
			if(Environment.GetEnvironmentVariable("NOMON") != null && Game.Dungeon?.Player != null){ var m = Game.Dungeon.Levels[Game.Dungeon.Player.LocationLevel]; int w = 0, sn = 0, lk = 0; for(int x = 0; x < m.width; x++) for(int y = 0; y < m.height; y++){ var q = m.mapSquares[x, y]; if(q.Walkable){ w++; if(q.SeenByPlayer) sn++; } if(q.Terrain == MapTerrain.ClosedLock) lk++; } foreach(var kv in Game.Dungeon.Locks) if(kv.Key.Level == Game.Dungeon.Player.LocationLevel) foreach(var l in kv.Value) Console.WriteLine("lock " + kv.Key.MapCoord + " " + l.GetType().Name + " open " + l.IsOpen() + " seen " + m.mapSquares[kv.Key.MapCoord.x, kv.Key.MapCoord.y].SeenByPlayer); Console.WriteLine("walkable seen " + sn + "/" + w + " locks " + lk + " elevators " + string.Join(",", Game.Dungeon.Features.OfType<Features.Elevator>().Where(e => e.LocationLevel == Game.Dungeon.Player.LocationLevel).Select(e => e.LocationMap + "->" + e.DestLevel + " seen " + m.mapSquares[e.LocationMap.x, e.LocationMap.y].SeenByPlayer))); }
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
