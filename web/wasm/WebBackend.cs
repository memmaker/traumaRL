/* RVIP web backend: runs inside a module Web Worker (web/worker.js).
   Keys: the page writes them into a SharedArrayBuffer ring; waitKey() blocks with
   Atomics.wait, so the game loop stays synchronous. Screen: the renderer's cell
   buffer + a JSON of text runs per present. */
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
namespace RogueBasin{
	public partial class WebBackend : IRvipBackend{
		[JSImport("present","trauma")] internal static partial void JsPresent([JSMarshalAs<JSType.MemoryView>] Span<int> cells,string info);
		[JSImport("waitKey","trauma")] internal static partial string JsWaitKey(int timeout_ms);
		[JSImport("sleep","trauma")] internal static partial void JsSleep(int ms);
		[JSImport("quit","trauma")] internal static partial void JsQuit();
		[JSImport("storeFile","trauma")] internal static partial void JsStoreFile(string name,[JSMarshalAs<JSType.MemoryView>] Span<byte> data);
		[JSImport("deleteFile","trauma")] internal static partial void JsDeleteFile(string name);
		[JSImport("initialFile","trauma")] internal static partial byte[] JsInitialFile(string name); //null: none
		[JSImport("sound","trauma")] internal static partial void JsSound(string name);
		public void Sound(string name){ JsSound(name); }
		[JSImport("beacon","trauma")] internal static partial void JsBeacon(string q);
		public void Beacon(string q){ JsBeacon(q); }
		public void FileChanged(string name){ if(File.Exists(name)) JsStoreFile(name, File.ReadAllBytes(name)); else JsDeleteFile(name); }
		public string WaitKey(int ms){ return JsWaitKey(ms); }
		public void Sleep(int ms){ JsSleep(ms); }
		public void Present(int[] cells, string info){ JsPresent(cells, info); }
		public void Quit(){ JsQuit(); while(true) JsWaitKey(-1); }
	}
	public static class WebMain{
		/// Writes the embedded data/ resources (config.txt) into the runtime's in-memory FS.
		public static void Unpack(){
			Assembly asm = Assembly.GetExecutingAssembly();
			foreach(string res in asm.GetManifestResourceNames()){
				if(!res.StartsWith("data/")) continue;
				using(Stream s = asm.GetManifestResourceStream(res)) using(FileStream f = File.Create(res.Substring(5))) s.CopyTo(f);
			}
		}
		public static void Main(string[] args){
			RvipInput.Backend = new WebBackend();
			Unpack();
			byte[] sav = WebBackend.JsInitialFile(RvipSave.File); //the page read it from IndexedDB
			if(sav != null && sav.Length > 0) File.WriteAllBytes(RvipSave.File, sav);
			//RVIP debug (page URL ?rviplocks): Shift+K / menu entry toggles all locks open; off in normal play
			foreach(string a in args) if(a.StartsWith("name=")) RvipInput.PlayerName = a.Substring(5); //RVIP 9
			if(Array.IndexOf(args, "rviplocks") >= 0) File.AppendAllText("config.txt", "\nrviplocks=true\n");
			//sentinel for the Mono interpreter's T[,] store bug (the game swallows exceptions and retries level generation forever)
			AppDomain.CurrentDomain.FirstChanceException += (o, e) => { if(e.Exception is ArrayTypeMismatchException) Console.WriteLine("ArrayTypeMismatchException (Mono T[,] store bug?)\n" + Environment.StackTrace); };
			try{ TraumaRL.RvipEntry.Run(); }
			catch(Exception e){ Console.WriteLine("TraumaRL crashed: " + e); throw; }
			RvipInput.Backend.Quit();
		}
	}
}
namespace TraumaRL{
	public static class RvipEntry{ public static void Run(){ new TraumaRunner().TemplatedMapTest(); } }
}
