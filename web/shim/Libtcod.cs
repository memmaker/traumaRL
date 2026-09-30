/* RVIP: managed replacement for the native parts of libtcod-net that TraumaRL
   calls (FOV, A* path finding, Bresenham lines, keyboard, root console).
   Enumerations.cs and TCODColor.cs are compiled from the original wrapper. */
using System;
using System.Collections.Generic;
namespace libtcodWrapper{
	internal static class DLLName{ internal const string name = "libtcod"; }

	/// FOV map (libtcod TCOD_map_*): recursive shadow casting, walls lit, radius 0 = unlimited.
	public class TCODFov : IDisposable{
		internal readonly int w, h;
		internal readonly bool[] transparent, walkable, fov;
		public TCODFov(int width, int height){ w = width; h = height; transparent = new bool[w*h]; walkable = new bool[w*h]; fov = new bool[w*h]; }
		public void ClearMap(){ Array.Clear(transparent,0,w*h); Array.Clear(walkable,0,w*h); Array.Clear(fov,0,w*h); }
		public void Dispose(){}
		public void SetCell(int x, int y, bool t, bool wk){ transparent[x+y*w] = t; walkable[x+y*w] = wk; }
		public void GetCell(int x, int y, out bool t, out bool wk){ t = transparent[x+y*w]; wk = walkable[x+y*w]; }
		internal bool Walkable(int x, int y){ return x >= 0 && y >= 0 && x < w && y < h && walkable[x+y*w]; }
		public bool CheckTileFOV(int x, int y){ return x >= 0 && y >= 0 && x < w && y < h && fov[x+y*w]; }
		static readonly int[,] oct = { {1,0,0,1},{0,1,1,0},{0,-1,1,0},{-1,0,0,1},{-1,0,0,-1},{0,-1,-1,0},{0,1,-1,0},{1,0,0,-1} };
		public void CalculateFOV(int px, int py, int radius){
			Array.Clear(fov,0,w*h);
			if(px < 0 || py < 0 || px >= w || py >= h) return;
			fov[px+py*w] = true;
			int r = radius > 0 ? radius : Math.Max(w,h);
			for(int o = 0; o < 8; o++) Cast(px,py,1,1.0,0.0,r,oct[o,0],oct[o,1],oct[o,2],oct[o,3]);
		}
		void Cast(int cx, int cy, int row, double start, double end, int r, int xx, int xy, int yx, int yy){
			if(start < end) return;
			int r2 = r*r;
			for(int j = row; j <= r; j++){
				int dy = -j; bool blocked = false; double newStart = 0;
				for(int dx = -j; dx <= 0; dx++){
					double ls = (dx-0.5)/(dy+0.5), rs = (dx+0.5)/(dy-0.5);
					if(start < rs) continue;
					if(end > ls) break;
					int x = cx + dx*xx + dy*xy, y = cy + dx*yx + dy*yy;
					bool inside = x >= 0 && y >= 0 && x < w && y < h;
					if(inside && dx*dx+dy*dy <= r2) fov[x+y*w] = true;
					bool opaque = !inside || !transparent[x+y*w];
					if(blocked){
						if(opaque){ newStart = rs; continue; }
						blocked = false; start = newStart;
					}else if(opaque && j < r){
						blocked = true;
						Cast(cx,cy,j+1,start,ls,r,xx,xy,yx,yy);
						newStart = rs;
					}
				}
				if(blocked) break;
			}
		}
	}

	public delegate float TCODPathCallback(int xFrom, int yFrom, int xTo, int yTo);

	/// A* (libtcod TCOD_path_*): 8 directions, diagonal cost, path excludes the origin.
	public class TCODPathFinding : IDisposable{
		readonly int w, h; readonly double diag; readonly TCODFov map; readonly TCODPathCallback cb;
		int ox, oy, dx, dy; List<int> path = new List<int>(); //steps origin->dest, popped from the front
		int head;
		public TCODPathFinding(int width, int height, double diagonalCost, TCODPathCallback callback){ w = width; h = height; diag = diagonalCost; cb = callback; }
		public TCODPathFinding(TCODFov fovMap, double diagonalCost){ map = fovMap; w = fovMap.w; h = fovMap.h; diag = diagonalCost; }
		public void Dispose(){}
		float Cost(int fx, int fy, int tx, int ty){
			if(tx < 0 || ty < 0 || tx >= w || ty >= h) return 0;
			if(map != null) return map.walkable[tx+ty*w] ? 1f : 0f;
			return cb(fx,fy,tx,ty);
		}
		public bool ComputePath(int origX, int origY, int destX, int destY){
			ox = origX; oy = origY; dx = destX; dy = destY; path.Clear(); head = 0;
			if(ox == dx && oy == dy) return true;
			if(ox < 0 || oy < 0 || ox >= w || oy >= h || dx < 0 || dy < 0 || dx >= w || dy >= h) return false;
			var g = new float[w*h]; var from = new int[w*h]; var closed = new bool[w*h];
			for(int i = 0; i < g.Length; i++){ g[i] = float.MaxValue; from[i] = -1; }
			var pq = new PriorityQueue<int,float>();
			int s = ox+oy*w, goal = dx+dy*w; g[s] = 0; pq.Enqueue(s,0);
			while(pq.Count > 0){
				int c = pq.Dequeue();
				if(closed[c]) continue;
				closed[c] = true;
				if(c == goal) break;
				int cx = c % w, cy = c / w;
				for(int ddx = -1; ddx <= 1; ddx++) for(int ddy = -1; ddy <= 1; ddy++){
					if(ddx == 0 && ddy == 0) continue;
					int nx = cx+ddx, ny = cy+ddy;
					if(nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
					int n = nx+ny*w; if(closed[n]) continue;
					float cost = Cost(cx,cy,nx,ny); if(cost <= 0) continue;
					float ng = g[c] + cost * (ddx != 0 && ddy != 0 ? (float)diag : 1f);
					if(ng < g[n]){
						g[n] = ng; from[n] = c;
						int hx = Math.Abs(nx-dx), hy = Math.Abs(ny-dy);
						float hh = (float)(Math.Min(hx,hy)*Math.Min(diag,2.0) + Math.Abs(hx-hy));
						pq.Enqueue(n, ng + hh);
					}
				}
			}
			if(from[goal] < 0) return false;
			for(int c = goal; c != s; c = from[c]) path.Add(c);
			path.Reverse();
			return true;
		}
		public bool WalkPath(ref int x, ref int y, bool recalculateWhenNeeded){
			if(head >= path.Count) return false;
			int n = path[head], nx = n % w, ny = n / w;
			if(Cost(x,y,nx,ny) <= 0){
				if(!recalculateWhenNeeded) return false;
				if(!ComputePath(x,y,dx,dy) || head >= path.Count) return false;
				n = path[head]; nx = n % w; ny = n / w;
			}
			head++; x = nx; y = ny; ox = nx; oy = ny;
			return true;
		}
		public void GetPointOnPath(int index, out int x, out int y){ int n = path[head+index]; x = n % w; y = n / w; }
		public bool IsPathEmpty(){ return head >= path.Count; }
		public int GetPathSize(){ return path.Count - head; }
		public void GetPathOrigin(out int x, out int y){ x = ox; y = oy; }
		public void GetPathDestination(out int x, out int y){ x = dx; y = dy; }
	}

	/// Bresenham line, same stepping as libtcod 1.4 TCOD_line_init/step.
	public static class TCODLineDrawing{
		static int stepx, stepy, e, deltax, deltay, origx, origy, destx, desty;
		public static void InitLine(int xFrom, int yFrom, int xTo, int yTo){
			origx = xFrom; origy = yFrom; destx = xTo; desty = yTo;
			deltax = xTo - xFrom; deltay = yTo - yFrom;
			stepx = Math.Sign(deltax); stepy = Math.Sign(deltay);
			e = stepx*deltax > stepy*deltay ? stepx*deltax : stepy*deltay;
			deltax *= 2; deltay *= 2;
		}
		public static bool StepLine(ref int xCur, ref int yCur){
			if(stepx*deltax > stepy*deltay){
				if(origx == destx) return true;
				origx += stepx; e -= stepy*deltay;
				if(e < 0){ origy += stepy; e += stepx*deltax; }
			}else{
				if(origy == desty) return true;
				origy += stepy; e -= stepx*deltax;
				if(e < 0){ origx += stepx; e += stepy*deltay; }
			}
			xCur = origx; yCur = origy;
			return false;
		}
	}

	public struct KeyPress{
		KeyCode keyCode; byte character; bool shift, ctrl, alt, pressed;
		public KeyPress(KeyCode code, byte ch, bool shift, bool ctrl, bool alt){ keyCode = code; character = ch; this.shift = shift; this.ctrl = ctrl; this.alt = alt; pressed = code != KeyCode.TCODK_NONE; }
		public KeyCode KeyCode => keyCode;
		public byte Character => character;
		public bool Pressed => pressed;
		public bool Alt => alt; public bool Control => ctrl;
		public bool LeftAlt => alt; public bool LeftControl => ctrl;
		public bool RightAlt => false; public bool RightControl => false;
		public bool Shift => shift;
	}

	/// Modal key reads: the same key queue as the SdlDotNet event loop (RvipInput).
	public static class Keyboard{
		public static KeyPress WaitForKeyPress(bool flushInputBuffer){ return RogueBasin.RvipInput.NextKey(-1).ToTcod(); }
		public static KeyPress CheckForKeypress(KeyPressType pressFlags){
			var k = RogueBasin.RvipInput.NextKey(0);
			return k == null ? new KeyPress() : k.ToTcod();
		}
		public static bool IsKeyPressed(KeyCode key){ return false; }
		public static void SetRepeat(int initialDelay, int interval){}
		public static void DisableRepeat(){}
	}

	public static class TCODSystem{
		public static void Sleep(uint milliseconds){ RogueBasin.RvipInput.Sleep((int)milliseconds); }
	}

	public class CustomFontRequest{ public CustomFontRequest(string fontFile, int w, int h, CustomFontRequestFontTypes type){} }
	public class Background{
		public static readonly Background None = new Background(BackgroundFlag.None);
		public static readonly Background Set = new Background(BackgroundFlag.Set);
		public Background(BackgroundFlag flag){}
		public Background(BackgroundFlag flag, float v){}
	}

	/// Root console: TraumaRL draws through IMapRenderer; these calls are unused leftovers (intro, fullscreen) and go nowhere.
	public class Console : IDisposable{
		public void Dispose(){}
		public Color ForegroundColor{ get; set; }
		public Color BackgroundColor{ get; set; }
		public void Clear(){}
		public void PutChar(int x, int y, char c){} public void PutChar(int x, int y, char c, Background f){}
		public void PrintLine(string s, int x, int y, LineAlignment a){}
		public void PrintLine(string s, int x, int y, Background f, LineAlignment a){}
		public int PrintLineRect(string s, int x, int y, int w, int h, LineAlignment a){ return 1; }
		public int PrintLineRect(string s, int x, int y, int w, int h, Background f, LineAlignment a){ return 1; }
		public void DrawFrame(int x, int y, int w, int h, bool clear){}
		public void DrawFrame(int x, int y, int w, int h, bool clear, string s){}
		public void DrawRect(int x, int y, int w, int h, bool clear){}
		public void SetCharBackground(int x, int y, Color c){} public void SetCharForeground(int x, int y, Color c){}
		public void Blit(int xs, int ys, int ws, int hs, Console d, int xd, int yd){}
	}
	public class RootConsole : Console{
		static RootConsole inst = new RootConsole();
		public static RootConsole GetInstance(){ return inst; }
		public static int Width{ get; set; } public static int Height{ get; set; }
		public static string WindowTitle{ get; set; } public static bool Fullscreen{ get; set; }
		public static CustomFontRequest Font{ get; set; }
		public bool IsWindowClosed(){ return false; }
		public void Flush(){}
		public void SetFullscreen(bool f){} public bool IsFullscreen(){ return false; }
		public static Console GetNewConsole(int w, int h){ return new Console(); }
	}
}
