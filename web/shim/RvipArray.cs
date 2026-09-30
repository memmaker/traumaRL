/* RVIP: Mono's wasm interpreter throws ArrayTypeMismatchException on stores into a
   generic T[,] (T = enum/int here), which the game's level generator swallows and
   retries forever. Game code stores through this helper instead (same bounds errors). */
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
namespace RogueBasin{
	public static class RvipArray{
		public static void Set<T>(T[,] a, int i, int j, T v){
			int h = a.GetLength(1);
			if((uint)i >= (uint)a.GetLength(0) || (uint)j >= (uint)h) throw new IndexOutOfRangeException();
			Unsafe.Add(ref Unsafe.As<byte, T>(ref MemoryMarshal.GetArrayDataReference(a)), i * h + j) = v;
		}
	}
}
