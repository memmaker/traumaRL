// RVIP: auto-explore ('e') and walk-to-elevator ('<' / '>'), one step per game turn.
// Uses only what the player knows (MapSquare.SeenByPlayer). Hooked in RogueBase's tick
// (ApplicationTickEventHandler) and key handler; any key stops it.
using System;
using System.Collections.Generic;
using System.Linq;

namespace RogueBasin
{
    public partial class RogueBase
    {
        enum AutoMode { None, Explore, ExitUp, ExitDown }
        AutoMode autoMode = AutoMode.None;
        bool autoSwallowKeyUp;
        int autoMsgBase, autoLevel, autoMonstersAtStart;
        Point autoLastPos, autoLastStep;
        readonly Dictionary<int, HashSet<Point>> autoVisited = new Dictionary<int, HashSet<Point>>();
        readonly HashSet<Item> autoSeenItems = new HashSet<Item>();
        int autoSeenItemsLevel = -1;

        static readonly int[] adx = { 0, 1, 0, -1, 1, 1, -1, -1 };
        static readonly int[] ady = { -1, 0, 1, 0, -1, 1, 1, -1 };

        /// Key handler hook: returns true when the key was consumed (stop or swallowed release).
        bool AutoKeyIntercept(SdlDotNet.Input.KeyboardEventArgs args)
        {
            if (autoMode != AutoMode.None)
            {
                autoMode = AutoMode.None;
                autoSwallowKeyUp = args.Down;
                return true;
            }
            if (!args.Down && autoSwallowKeyUp) { autoSwallowKeyUp = false; return true; }
            return false;
        }

        List<Monster> AutoVisibleMonsters()
        {
            var d = Game.Dungeon; var p = d.Player; var map = d.Levels[p.LocationLevel];
            return d.Monsters.Where(m => m.LocationLevel == p.LocationLevel && m.Alive
                && map.mapSquares[m.LocationMap.x, m.LocationMap.y].InPlayerFOV).ToList();
        }

        IEnumerable<Item> AutoVisibleItems()
        {
            var d = Game.Dungeon; var p = d.Player; var map = d.Levels[p.LocationLevel];
            return d.Items.Where(i => !i.InInventory && i.LocationLevel == p.LocationLevel
                && map.mapSquares[i.LocationMap.x, i.LocationMap.y].InPlayerFOV);
        }

        /// '<' / '>' / 'e' pressed in map mode. Returns time-advances.
        bool AutoStart(char key)
        {
            var p = Game.Dungeon.Player;
            autoMode = key == 'e' ? AutoMode.Explore : key == '<' ? AutoMode.ExitUp : AutoMode.ExitDown;
            autoLevel = p.LocationLevel;
            autoMsgBase = Game.MessageQueue.AddedCount;
            autoMonstersAtStart = AutoVisibleMonsters().Count;
            autoLastPos = new Point(-1, -1);
            if (autoSeenItemsLevel != p.LocationLevel) { autoSeenItems.Clear(); autoSeenItemsLevel = p.LocationLevel; }
            foreach (var i in AutoVisibleItems()) autoSeenItems.Add(i);
            if (autoMode == AutoMode.Explore)
            {
                var m = AutoVisibleMonsters();
                if (m.Count > 0) { AutoStop("In view: " + m[0].SingleDescription + "."); return false; }
            }
            // Elevator adjacent at the key press: take it
            if (autoMode != AutoMode.Explore)
            {
                var e = AutoElevators().FirstOrDefault(x => Utility.GetDistanceBetween(x.LocationMap, p.LocationMap) < 1.5);
                if (e != null)
                {
                    autoMode = AutoMode.None;
                    return Game.Dungeon.PCMove(e.LocationMap.x - p.LocationMap.x, e.LocationMap.y - p.LocationMap.y);
                }
            }
            return AutoStep();
        }

        void AutoStop(string msg)
        {
            autoMode = AutoMode.None;
            if (msg != null) Game.MessageQueue.AddMessage(msg);
            Screen.Instance.NeedsUpdate = true;
        }

        IEnumerable<UseableFeature> AutoElevators()
        {
            var d = Game.Dungeon; var p = d.Player; var map = d.Levels[p.LocationLevel];
            //the escape pod is the station's last exit: > walks to it once known
            var pods = d.Features.OfType<Features.EscapePod>().Where(f => f.LocationLevel == p.LocationLevel
                && map.mapSquares[f.LocationMap.x, f.LocationMap.y].SeenByPlayer).ToList();
            if (autoMode == AutoMode.ExitDown && pods.Count > 0) return pods;
            var all = d.Features.OfType<Features.Elevator>().Where(f => f.LocationLevel == p.LocationLevel
                && map.mapSquares[f.LocationMap.x, f.LocationMap.y].SeenByPlayer).ToList();
            IEnumerable<Features.Elevator> pref = autoMode == AutoMode.ExitUp ? all.Where(f => f.DestLevel < p.LocationLevel)
                : all.Where(f => f.DestLevel > p.LocationLevel);
            return pref.Any() ? pref : all;
        }

        /// Tick hook: one step when the game waits for the player.
        void AutoTick()
        {
            if (autoMode == AutoMode.None || waitingForTurnTick) return;
            if (inputState != InputState.MapMovement || !Game.Dungeon.RunMainLoop) { autoMode = AutoMode.None; return; }
            if (AutoStep())
            {
                Game.Dungeon.PlayerHadBonusTurn = true;
                waitingForTurnTick = true;
                Screen.Instance.CenterViewOnPoint(Game.Dungeon.Player.LocationLevel, Game.Dungeon.Player.LocationMap);
                Screen.Instance.NeedsUpdate = true;
            }
        }

        bool AutoStep()
        {
            var d = Game.Dungeon; var p = d.Player; int lvl = p.LocationLevel;
            if (lvl != autoLevel) { AutoStop(null); return false; }
            if (Game.MessageQueue.AddedCount != autoMsgBase) { AutoStop(null); return false; }
            var mons = AutoVisibleMonsters();
            if (autoMode == AutoMode.Explore ? mons.Count > 0 : mons.Count > autoMonstersAtStart)
            {
                var m = mons[0];
                AutoStop("In view: " + m.SingleDescription + "."); return false;
            }
            autoMonstersAtStart = Math.Min(autoMonstersAtStart, mons.Count);
            if (autoMode == AutoMode.Explore)
            {
                var newItem = AutoVisibleItems().FirstOrDefault(i => !autoSeenItems.Contains(i));
                if (newItem != null)
                {
                    foreach (var i in AutoVisibleItems()) autoSeenItems.Add(i);
                    AutoStop("You see: " + newItem.SingleItemDescription + "."); return false;
                }
            }
            var map = d.Levels[lvl];
            // A step that did not move (and did not open a door) stops the walk
            if (autoLastPos == p.LocationMap && !(map.mapSquares[autoLastStep.x, autoLastStep.y].Terrain == MapTerrain.OpenDoor))
            { AutoStop(null); return false; }

            if (!autoVisited.ContainsKey(lvl)) autoVisited[lvl] = new HashSet<Point>();
            var visited = autoVisited[lvl];
            visited.Add(p.LocationMap);

            var elevators = autoMode == AutoMode.Explore ? new List<UseableFeature>() : AutoElevators().ToList();
            if (autoMode != AutoMode.Explore && elevators.Count == 0) { AutoStop("You don't know of an elevator on this level."); return false; }
            var elevatorCells = new HashSet<Point>(elevators.Select(e => e.LocationMap));
            if (elevatorCells.Any(e => Utility.GetDistanceBetween(e, p.LocationMap) < 1.5))
            { AutoStop("Press " + (autoMode == AutoMode.ExitUp ? "<" : ">") + " again to take the elevator."); return false; }

            var itemCells = new HashSet<Point>(d.Items.Where(i => !i.InInventory && i.LocationLevel == lvl
                && map.mapSquares[i.LocationMap.x, i.LocationMap.y].SeenByPlayer).Select(i => i.LocationMap));

            Func<int, int, bool> known = (x, y) => x >= 0 && y >= 0 && x < map.width && y < map.height && map.mapSquares[x, y].SeenByPlayer;
            Func<int, int, bool> passable = (x, y) =>
            {
                if (!known(x, y)) return false;
                var t = map.mapSquares[x, y].Terrain;
                if (t == MapTerrain.ClosedLock) return AutoCanUnlock(d, p, lvl, new Point(x, y));
                if (!map.mapSquares[x, y].Walkable && t != MapTerrain.ClosedDoor) return false;
                if (d.FeatureAtSpace(lvl, new Point(x, y)) is UseableFeature) return false; // elevators etc: never auto-step
                return true;
            };
            Func<int, int, bool> isGoal = (x, y) =>
            {
                var pt = new Point(x, y);
                if (autoMode != AutoMode.Explore)
                    return elevatorCells.Any(e => Utility.GetDistanceBetween(e, pt) < 1.5);
                if (visited.Contains(pt)) return false;
                if (itemCells.Contains(pt)) return true;
                for (int k = 0; k < 8; k++)
                {
                    int nx = x + adx[k], ny = y + ady[k];
                    if (nx >= 0 && ny >= 0 && nx < map.width && ny < map.height && !map.mapSquares[nx, ny].SeenByPlayer) return true;
                }
                return false;
            };

            // BFS
            var prev = new Dictionary<Point, Point>();
            var q = new Queue<Point>();
            var start = p.LocationMap;
            prev[start] = start; q.Enqueue(start);
            Point goal = null;
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                if (c != start && isGoal(c.x, c.y)) { goal = c; break; }
                if (autoMode == AutoMode.Explore && c != start && (map.mapSquares[c.x, c.y].Terrain == MapTerrain.ClosedDoor || map.mapSquares[c.x, c.y].Terrain == MapTerrain.ClosedLock)) continue; // explore: expand beyond after opening; elevator walk: plan through doors (bumping opens them)
                for (int k = 0; k < 8; k++)
                {
                    var n = new Point(c.x + adx[k], c.y + ady[k]);
                    if (prev.ContainsKey(n) || !passable(n.x, n.y)) continue;
                    prev[n] = c; q.Enqueue(n);
                }
            }
            if (goal == null)
            {
                bool locks = false;
                for (int x = 0; x < map.width && !locks; x++) for (int y = 0; y < map.height; y++)
                        if (map.mapSquares[x, y].SeenByPlayer && map.mapSquares[x, y].Terrain == MapTerrain.ClosedLock && !AutoCanUnlock(d, p, lvl, new Point(x, y))) { locks = true; break; }
                AutoStop(autoMode == AutoMode.Explore
                    ? (locks ? "Nothing left to explore that isn't behind a locked door." : "Nothing left to explore.")
                    : "No known way to the elevator.");
                return false;
            }
            var s = goal;
            while (prev[s] != start) s = prev[s];
            autoLastPos = start; autoLastStep = s;
            autoMsgBase = Game.MessageQueue.AddedCount;
            bool moved = d.PCMove(s.x - start.x, s.y - start.y);
            return moved;
        }
    
        // A known lock the player holds the key cards for is walked into (bumping opens it).
        static bool AutoCanUnlock(Dungeon d, Player p, int lvl, Point pt)
        {
            var ls = d.LocksAtLocation(lvl, pt);
            return ls.Count > 0 && (d.AllLocksOpen || ls.All(l => l.IsOpen() || (l is Locks.SimpleLockedDoor && ((Locks.SimpleLockedDoor)l).CanDoorBeOpenedWithClues(p))));
        }
}
}
