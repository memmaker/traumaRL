/* RVIP: save / resume (one slot, roguelike: deleted at death or win).
   Upstream's SaveGame() (XmlSerializer over SaveGameInfo) throws and misses TraumaRL state, so
   this writes the whole game state graph (Dungeon incl. player, levels, monsters, items, locks,
   map info; the message queue; the RNG) with BinaryFormatter. Classes are not marked
   [Serializable]: FieldSurrogate copies every instance field of any non-serializable type by
   reflection; delegates, HashSet/Dictionary and System.Type get their own surrogates
   (.NET 10 / browser-wasm can't write them directly). Written atomically (tmp + move). */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

namespace RogueBasin
{
    public partial class RogueBase
    {
        /// Safe point for a save: main loop, map command, no menu, no auto-explore, dungeon turn done.
        public bool RvipCanSave { get { return RvipAtCmd && autoMode == AutoMode.None && !waitingForTurnTick; } }
        /// After a load: the event loop starts with the player to move.
        public void RvipResumed() { waitingForTurnTick = false; firstRun = true; }
    }

    public static class RvipSave
    {
        public const string File = "traumarl.sav";
        public static bool Enabled = true;
        /// Work left for after Deserialize: fill collections, set delegate fields.
        internal static List<Action> Pending = new List<Action>();

        public static bool Exists { get { return System.IO.File.Exists(File); } }

        static BinaryFormatter Formatter()
        {
            return new BinaryFormatter { SurrogateSelector = new RvipSurrogateSelector() };
        }

        public static void Save()
        {
            if (!Enabled) return;
            string tmp = File + ".tmp";
            try
            {
                using (var f = System.IO.File.Create(tmp))
                    Formatter().Serialize(f, new object[] { Game.Dungeon, Game.MessageQueue, Game.Random });
                System.IO.File.Move(tmp, File, true);
                RvipInput.Backend.FileChanged(File);
            }
            catch (Exception e)
            {
                Console.WriteLine("RVIP save failed: " + e);
                try { System.IO.File.Delete(tmp); } catch { }
            }
        }

        public static bool Load()
        {
            try
            {
                object[] g;
                Pending.Clear();
                using (var f = System.IO.File.OpenRead(File))
                    g = (object[])Formatter().Deserialize(f);
                foreach (var a in Pending) a();
                Pending.Clear();
                Game.Dungeon = (Dungeon)g[0];
                Game.MessageQueue = (MessageQueue)g[1];
                Game.Random = (Random)g[2];
                savedClock = Game.Dungeon.WorldClock;
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine("RVIP load failed: " + e);
                Delete();
                return false;
            }
        }

        /// Debug (native test): round-trip one object in memory, report the error.
        public static string RoundTrip(object o)
        {
            try { var ms = new MemoryStream(); Formatter().Serialize(ms, o); ms.Position = 0; Pending.Clear(); Formatter().Deserialize(ms); foreach (var a in Pending) a(); Pending.Clear(); return "ok " + ms.Length; }
            catch (Exception e) { return e.GetType().Name + ": " + e.Message; }
        }

        public static void Delete()
        {
            over = true;
            if (!Exists) return;
            try { System.IO.File.Delete(File); } catch { }
            RvipInput.Backend.FileChanged(File);
        }

        /// The page asks (tab hidden / closed): save only at a safe point.
        /// Only when time moved since the last save.
        static long savedClock = -1; static bool over;
        /// The run moved on since the last save (the page warns before unload).
        public static bool Unsaved { get { try { return Enabled && !over && Game.Dungeon.Player != null && !Game.Dungeon.PlayerDeathOccured && Game.Dungeon.WorldClock != savedClock; } catch { return false; } } }
        public static void Request()
        {
            if (Game.Base == null || !Game.Base.RvipCanSave || Game.Dungeon.PlayerDeathOccured) return;
            if (Game.Dungeon.WorldClock == savedClock && Exists) return;
            var t = System.Diagnostics.Stopwatch.StartNew();
            Save();
            savedClock = Game.Dungeon.WorldClock;
            Screen.Instance.NeedsUpdate = true; //present again: the page learns "unsaved" is off
            Console.WriteLine("RVIP saved in " + t.ElapsedMilliseconds + " ms");
        }
    }

    sealed class RvipSurrogateSelector : ISurrogateSelector
    {
        ISurrogateSelector next;
        public void ChainSelector(ISurrogateSelector selector) { next = selector; }
        public ISurrogateSelector GetNextSelector() { return next; }
        public ISerializationSurrogate GetSurrogate(Type type, StreamingContext context, out ISurrogateSelector selector)
        {
            selector = this;
            if (typeof(Delegate).IsAssignableFrom(type)) return DelegateSurrogate.Instance;
            if (typeof(Type).IsAssignableFrom(type)) return TypeSurrogate.Instance;
            if (CollectionSurrogate.Base(type) != null) return CollectionSurrogate.Instance;
            if (!type.IsSerializable && !type.IsArray && !type.IsPrimitive && !type.IsEnum && !type.IsPointer && type != typeof(string))
                return FieldSurrogate.Instance;
            selector = null;
            return null;
        }
    }

    /// Any class/struct not marked [Serializable]: all instance fields up the hierarchy.
    public sealed class FieldSurrogate : ISerializationSurrogate
    {
        public static readonly FieldSurrogate Instance = new FieldSurrogate();
        static readonly Dictionary<Type, FieldInfo[]> cache = new Dictionary<Type, FieldInfo[]>();
        static FieldInfo[] Fields(Type t)
        {
            FieldInfo[] r;
            if (cache.TryGetValue(t, out r)) return r;
            var l = new List<FieldInfo>();
            for (Type b = t; b != null && b != typeof(object); b = b.BaseType)
                l.AddRange(b.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(f => !f.IsNotSerialized));
            return cache[t] = l.ToArray();
        }
        static string Name(FieldInfo f) { return f.DeclaringType.Name + "+" + f.Name; }
        public static Dictionary<string, int> Stats;
        public void GetObjectData(object obj, SerializationInfo info, StreamingContext context)
        {
            if (Stats != null) { string k = obj.GetType().FullName; Stats[k] = Stats.TryGetValue(k, out int c) ? c + 1 : 1; }
            foreach (var f in Fields(obj.GetType()))
            {
                object v = f.GetValue(obj);
                if (v is Delegate) v = new DelegateRec((Delegate)v);
                info.AddValue(Name(f), v, typeof(object));
            }
        }
        public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector selector)
        {
            var have = new HashSet<string>();
            foreach (SerializationEntry e in info) have.Add(e.Name);
            foreach (var f in Fields(obj.GetType()))
                if (have.Contains(Name(f)))
                {
                    object v = info.GetValue(Name(f), typeof(object));
                    if (v is DelegateRec) { var r = (DelegateRec)v; var fi = f; RvipSave.Pending.Add(() => fi.SetValue(obj, r.Make())); }
                    else f.SetValue(obj, v);
                }
            return obj;
        }
    }

    /// A delegate held in a field: method + target, made again after the graph is complete.
    [Serializable]
    sealed class DelegateRec
    {
        string type; string[] decl, name, sig, gen; int[] tok; object[] target;
        public DelegateRec(Delegate d)
        {
            var l = d.GetInvocationList(); int n = l.Length;
            type = d.GetType().AssemblyQualifiedName; decl = new string[n]; name = new string[n]; sig = new string[n]; gen = new string[n]; tok = new int[n]; target = new object[n];
            for (int i = 0; i < n; i++)
            {
                var m = l[i].Method;
                decl[i] = m.DeclaringType.AssemblyQualifiedName; name[i] = m.Name; sig[i] = DelegateSurrogate.Sig(m); tok[i] = m.MetadataToken;
                gen[i] = m.IsGenericMethod ? string.Join("|", m.GetGenericArguments().Select(a => a.AssemblyQualifiedName)) : "";
                target[i] = l[i].Target;
            }
        }
        public Delegate Make()
        {
            Type t = Type.GetType(type, true); Delegate r = null;
            for (int i = 0; i < decl.Length; i++)
            {
                var all = Type.GetType(decl[i], true).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
                int ii = i;
                MethodInfo m = all.FirstOrDefault(x => x.MetadataToken == tok[ii] && x.Name == name[ii]) ?? all.First(x => x.Name == name[ii] && DelegateSurrogate.Sig(x) == sig[ii]);
                if (gen[i] != "" && m.IsGenericMethodDefinition) m = m.MakeGenericMethod(gen[i].Split('|').Select(a => Type.GetType(a, true)).ToArray());
                r = Delegate.Combine(r, m.IsStatic ? Delegate.CreateDelegate(t, m) : Delegate.CreateDelegate(t, target[i], m));
            }
            return r;
        }
    }

    sealed class TypeSurrogate : ISerializationSurrogate
    {
        public static readonly TypeSurrogate Instance = new TypeSurrogate();
        public void GetObjectData(object obj, SerializationInfo info, StreamingContext context)
        {
            info.SetType(typeof(TypeHolder));
            info.AddValue("n", ((Type)obj).AssemblyQualifiedName);
        }
        public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector selector) { throw new NotSupportedException(); }
    }
    [Serializable]
    sealed class TypeHolder : ISerializable, IObjectReference
    {
        readonly string n;
        TypeHolder(SerializationInfo info, StreamingContext context) { n = info.GetString("n"); }
        public void GetObjectData(SerializationInfo info, StreamingContext context) { throw new NotSupportedException(); }
        public object GetRealObject(StreamingContext context) { return Type.GetType(n, true); }
    }

    sealed class DelegateSurrogate : ISerializationSurrogate
    {
        public static readonly DelegateSurrogate Instance = new DelegateSurrogate();
        public void GetObjectData(object obj, SerializationInfo info, StreamingContext context)
        {
            Delegate[] list = ((Delegate)obj).GetInvocationList();
            info.SetType(typeof(DelegateHolder));
            info.AddValue("type", obj.GetType().AssemblyQualifiedName);
            info.AddValue("count", list.Length);
            for (int i = 0; i < list.Length; i++)
            {
                MethodInfo m = list[i].Method;
                info.AddValue("decl" + i, m.DeclaringType.AssemblyQualifiedName);
                info.AddValue("name" + i, m.Name);
                info.AddValue("sig" + i, Sig(m));
                info.AddValue("tok" + i, m.MetadataToken);
                info.AddValue("gen" + i, m.IsGenericMethod ? string.Join("|", m.GetGenericArguments().Select(a => a.AssemblyQualifiedName)) : "");
                info.AddValue("target" + i, list[i].Target, typeof(object));
            }
        }
        public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector selector) { throw new NotSupportedException(); }
        internal static string Sig(MethodInfo m) { return string.Join(",", m.GetParameters().Select(p => p.ParameterType.FullName)); }
    }
    [Serializable]
    sealed class DelegateHolder : ISerializable, IObjectReference
    {
        readonly SerializationInfo info;
        DelegateHolder(SerializationInfo info, StreamingContext context) { this.info = info; }
        public void GetObjectData(SerializationInfo i, StreamingContext context) { throw new NotSupportedException(); }
        public object GetRealObject(StreamingContext context)
        {
            Type type = Type.GetType(info.GetString("type"), true);
            Delegate r = null;
            for (int i = 0, n = info.GetInt32("count"); i < n; i++)
            {
                Type decl = Type.GetType(info.GetString("decl" + i), true);
                string name = info.GetString("name" + i), sig = info.GetString("sig" + i);
                int tok = info.GetInt32("tok" + i); string gen = info.GetString("gen" + i);
                var all = decl.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
                MethodInfo m = all.FirstOrDefault(x => x.MetadataToken == tok && x.Name == name) ?? all.First(x => x.Name == name && DelegateSurrogate.Sig(x) == sig);
                if (gen != "" && m.IsGenericMethodDefinition) m = m.MakeGenericMethod(gen.Split('|').Select(a => Type.GetType(a, true)).ToArray());
                object target = info.GetValue("target" + i, typeof(object));
                r = Delegate.Combine(r, m.IsStatic ? Delegate.CreateDelegate(type, m) : Delegate.CreateDelegate(type, target, m));
            }
            return r;
        }
    }

    /// HashSet / Dictionary: written as plain arrays; on load the same instance is constructed in place
    /// (no IObjectReference: those break on reference cycles) and filled after the whole graph is done.
    sealed class CollectionSurrogate : ISerializationSurrogate
    {
        public static readonly CollectionSurrogate Instance = new CollectionSurrogate();
        /// The HashSet<>/Dictionary<,>/SortedDictionary<,> this type is or derives from (QuikGraph's VertexEdgeDictionary does).
        internal static Type Base(Type t)
        {
            for (; t != null && t != typeof(object); t = t.BaseType)
                if (t.IsGenericType && (t.GetGenericTypeDefinition() == typeof(HashSet<>) || t.GetGenericTypeDefinition() == typeof(Dictionary<,>) || t.GetGenericTypeDefinition() == typeof(SortedDictionary<,>)))
                    return t;
            return null;
        }
        public void GetObjectData(object obj, SerializationInfo info, StreamingContext context)
        {
            var all = new List<object>();
            foreach (object o in (System.Collections.IEnumerable)obj) all.Add(o);
            if (Base(obj.GetType()).GetGenericArguments().Length == 2)
            {
                var k = new object[all.Count]; var v = new object[all.Count];
                for (int i = 0; i < all.Count; i++) { var pt = all[i].GetType(); k[i] = pt.GetProperty("Key").GetValue(all[i]); v[i] = pt.GetProperty("Value").GetValue(all[i]); }
                info.AddValue("keys", k); info.AddValue("values", v);
            }
            else info.AddValue("items", all.ToArray());
        }
        public object SetObjectData(object obj, SerializationInfo info, StreamingContext context, ISurrogateSelector selector)
        {
            Type t = Base(obj.GetType());
            obj.GetType().GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null).Invoke(obj, null);
            bool dict = t.GetGenericArguments().Length == 2;
            var k = (object[])info.GetValue(dict ? "keys" : "items", typeof(object[]));
            var v = dict ? (object[])info.GetValue("values", typeof(object[])) : null;
            var add = t.GetMethod("Add", t.GetGenericArguments());
            RvipSave.Pending.Add(() => { for (int i = 0; i < k.Length; i++) add.Invoke(obj, dict ? new[] { k[i], v[i] } : new[] { k[i] }); });
            return obj;
        }
    }
}
