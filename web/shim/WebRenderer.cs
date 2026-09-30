/* RVIP: replaces RogueBasin/MapRendererSDLDotNet.cs (same class name, so Screen.cs is
   unchanged). Instead of blitting SDL surfaces it keeps a cell buffer: per screen cell up
   to LAYERS sprites (TraumaSprites.png index, foreground RGB, background RGB or -1 =
   transparent). A sprite with an opaque background hides the layers under it. Strings
   (drawn by SDL_ttf in the original) are kept as text runs. Flush() hands both to the
   backend: cells as an int array, text runs as JSON. */
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
namespace RogueBasin{
	[System.Serializable] class MapRendererSDLDotNet : IMapRenderer{
		public const int Cols = 60, Rows = 45, LAYERS = 4, CELL = 1 + LAYERS * 3; //count, then (id, fg, bg) per layer
		public static readonly int[] Cells = new int[Cols * Rows * CELL];
		readonly List<(int x, int y, int rgb, string s)> texts = new List<(int, int, int, string)>();
		static readonly Color transparent = Color.FromArgb(255, 0, 255);
		static int Rgb(Color c){ return (c.R << 16) | (c.G << 8) | c.B; }
		string lastInfo; int[] lastCells = new int[Cells.Length];

		public void RenderMap(TileEngine.TileMap map, Point off, Rectangle vp){
			if(off.x >= map.Columns || off.y >= map.Rows) throw new Exception("Point outside map " + off);
			int maxC = Math.Min(off.x + vp.Width - 1, map.Columns - 1), maxR = Math.Min(off.y + vp.Height - 1, map.Rows - 1);
			foreach(TileEngine.TileLayer layer in map.Layer)
				for(int y = off.y; y <= maxR; y++)
					for(int x = off.x; x <= maxC; x++){
						TileEngine.TileCell c = layer.Rows[y].Columns[x];
						if(c.TileID == -1) continue;
						Color fg = Color.White, bg = transparent;
						if(c.TileFlag is LibtcodColorFlags f){ fg = f.ForegroundColor; bg = f.BackgroundColor; }
						DrawSprite(c.TileID, vp.X + (x - off.x), vp.Y + (y - off.y), fg, bg);
					}
		}
		void DrawSprite(int id, int x, int y, Color fg, Color bg){
			if(x < 0 || y < 0 || x >= Cols || y >= Rows) return;
			int o = (x + y * Cols) * CELL, n = Cells[o];
			bool opaque = bg != transparent && bg.A != 0;
			if(opaque) n = 0;
			if(n == LAYERS){ Array.Copy(Cells, o + 4, Cells, o + 1, (LAYERS - 1) * 3); n--; }
			int p = o + 1 + n * 3;
			Cells[p] = id; Cells[p + 1] = Rgb(fg); Cells[p + 2] = opaque ? Rgb(bg) : -1;
			Cells[o] = n + 1;
		}
		public void Sleep(ulong ms){}
		public void Setup(int width, int height){ Clear(); }
		bool full; //a whole-screen view is up (movie, history, end screen): DrawFrame(clear) since the last Clear()
		static int histSent;
		public void Flush(){
			var sb = new StringBuilder("{\"cols\":60,\"rows\":35,\"layers\":4,\"texts\":[");
			for(int i = 0; i < texts.Count; i++){
				var t = texts[i];
				if(i > 0) sb.Append(',');
				sb.Append('[').Append(t.x).Append(',').Append(t.y).Append(',').Append(t.rgb).Append(',').Append(Json(t.s)).Append(']');
			}
			sb.Append(']');
			if(RogueBase.RvipMenuJson != null) sb.Append(",\"menu\":").Append(RogueBase.RvipMenuJson);
			Panes(sb);
			sb.Append('}');
			string info = sb.ToString();
			if(info == lastInfo && Cells.AsSpan().SequenceEqual(lastCells)) return;
			lastInfo = info; Cells.CopyTo(lastCells, 0);
			RvipInput.Backend.Present(Cells, info);
		}
		/// RVIP stage 5: each web window gets its own content, decided here from the game's own screen areas
		/// (Screen.RvipMapRect / RvipStatsRect / RvipMsgRect): map rectangle, status lines (text runs + sprites
		/// in reading order), prompt (message rows), new message-history lines, inventory rows, at-command flag.
		void Panes(StringBuilder sb){
			var scr = Screen.Instance;
			if(scr == null) return;
			Rectangle m = scr.RvipMapRect, st = scr.RvipStatsRect, ms = scr.RvipMsgRect;
			sb.Append(",\"full\":").Append(full ? 1 : 0);
			sb.Append(",\"map\":[").Append(m.X).Append(',').Append(m.Y).Append(',').Append(m.Width).Append(',').Append(m.Height).Append(']');
			//status: per row, segments ["text", rgb] or [sprite id, rgb]; trailing empty rows dropped (RVIP W0 5)
			var rows = new List<string>();
			for(int y = st.Y; y < st.Bottom; y++){
				var segs = new SortedDictionary<int, (string j, int len)>(); var used = new bool[st.Width];
				foreach(var t in texts) if(t.y == y && t.x >= st.X && t.x < st.Right){
					string s = t.s.TrimEnd(); if(s.Length == 0) continue;
					segs[t.x] = ("[" + Json(s) + "," + t.rgb + "]", s.Length);
					for(int k = t.x - st.X; k < Math.Min(st.Width, t.x - st.X + s.Length); k++) used[k] = true;
				}
				for(int x = st.X; x < st.Right; x++){
					int o = (x + y * Cols) * CELL, n = Cells[o];
					if(n == 0 || used[x - st.X] || segs.ContainsKey(x)) continue;
					int p = o + 1 + (n - 1) * 3;
					if(Cells[p] == ' ') continue;
					segs[x] = ("[" + Cells[p] + "," + Cells[p + 1] + "]", 1);
				}
				var r = new StringBuilder("["); int col = st.X;
				foreach(var kv in segs){
					if(kv.Key < col) continue;
					if(r.Length > 1) r.Append(',');
					if(kv.Key > col) r.Append("[\"").Append(' ', kv.Key - col).Append("\",0],");
					r.Append(kv.Value.j);
					col = kv.Key + kv.Value.len;
				}
				rows.Add(r.Append(']').ToString());
			}
			while(rows.Count > 0 && rows[rows.Count - 1] == "[]") rows.RemoveAt(rows.Count - 1);
			sb.Append(",\"status\":[").Append(string.Join(",", rows)).Append(']');
			//prompt: the live message rows
			var pr = new List<string>();
			for(int y = ms.Y; y < ms.Bottom; y++){
				string line = string.Join(" ", texts.FindAll(t => t.y == y && t.x >= ms.X && t.x < ms.Right).ConvertAll(t => t.s.TrimEnd()));
				if(line.Length > 0) pr.Add(line);
			}
			sb.Append(",\"prompt\":").Append(Json(string.Join("\n", pr)));
			//messages: history lines added since the last present
			var h = Game.MessageQueue?.messageHistory;
			int add = MessageQueue.RvipHistoryAdded - histSent;
			if(h != null && add > 0){
				histSent = MessageQueue.RvipHistoryAdded;
				var lines = new List<string>(); int skip = Math.Max(0, h.Count - add), i = 0;
				foreach(var l in h) if(i++ >= skip) lines.Add(Json(l));
				sb.Append(",\"log\":[").Append(string.Join(",", lines)).Append(']');
			}
			if(Game.Base != null){
				sb.Append(",\"atCmd\":").Append(Game.Base.RvipAtCmd && !full ? 1 : 0);
				sb.Append(",\"unsaved\":").Append(RvipSave.Unsaved ? 1 : 0);
				string inv = "[]"; try{ inv = Game.Base.RvipInventoryJson(); } catch(Exception){ }
				sb.Append(",\"inv\":").Append(inv);
			}
		}
		public static string Json(string s){
			var sb = new StringBuilder("\"");
			foreach(char c in s){
				if(c == '"' || c == '\\') sb.Append('\\').Append(c);
				else if(c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
				else sb.Append(c);
			}
			return sb.Append('"').ToString();
		}
		public void Clear(){ Array.Clear(Cells, 0, Cells.Length); texts.Clear(); full = false; }
		public void DrawFrame(int tlx, int tly, int width, int height, bool clear, Color color){
			if(clear){ ClearRect(tlx, tly, width, height); full = true; }
			for(int x = tlx + 1; x < tlx + width - 1; x++){ PutChar(x, tly, '-', color); PutChar(x, tly + height - 1, '-', color); }
			for(int y = tly + 1; y < tly + height - 1; y++){ PutChar(tlx, y, '|', color); PutChar(tlx + width - 1, y, '|', color); }
			PutChar(tlx, tly, '+', color); PutChar(tlx + width - 1, tly, '+', color);
			PutChar(tlx, tly + height - 1, '+', color); PutChar(tlx + width - 1, tly + height - 1, '+', color);
		}
		public void PutChar(int x, int y, char c, Color color){ DrawSprite(c, x, y, color, transparent); }
		public void PrintStringRect(string msg, int x, int y, int width, int height, LineAlignment alignment, Color color){
			if(alignment == LineAlignment.Center){ x += Math.Max(width - msg.Length, 0) / 2; y += Math.Max(height - 1, 0) / 2; }
			PrintString(msg, x, y, color);
		}
		public void PrintString(string msg, int x, int y, Color color){ texts.Add((x, y, Rgb(color), msg)); }
		/// The original fills black only for DrawFrame(clear); ClearRect itself was a no-op there. Here both clear cells and texts.
		public void ClearRect(int x, int y, int width, int height){
			for(int j = Math.Max(y, 0); j < Math.Min(y + height, Rows); j++)
				for(int i = Math.Max(x, 0); i < Math.Min(x + width, Cols); i++) Cells[(i + j * Cols) * CELL] = 0;
			texts.RemoveAll(t => t.y >= y && t.y < y + height && t.x >= x && t.x < x + width);
		}
	}
}
