using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OnePieceTcg.Engine;
using ShippedClone = OnePieceTcg.Engine.Bot.Search.GameClone;
using ResearchClone = OnePieceTcg.Sim.Search.GameClone;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// GameClone.Clone is a HAND-WRITTEN deep copy, so every field added to GameState has to be
    /// remembered there too. That is the same drift shape as the reveal-cost bug - a second
    /// implementation of a rule falling behind the first - except the consequence is worse: the bot
    /// searches on clones, Sandbox undo/redo restores from them, and the puzzle LethalSolver plans on
    /// them. A dropped field means all three work from a board that is not the one being played.
    ///
    /// It found 16 missing fields on the first run. Measured after the fix: turn-state was non-empty in
    /// 12.9% of search clones and a postponed removal in 2.3%, so this was not a theoretical gap.
    ///
    /// Checking by eye does not scale and does not survive the next field, so this walks the types by
    /// REFLECTION. Fields it cannot populate generically are REPORTED as unchecked rather than skipped
    /// quietly - LastPowerBuffTargetId was hiding in that bucket, and an unchecked field is not a
    /// passing field.
    ///
    /// Both cloners are checked. Engine.Bot.Search is what ships; Sim.Search is a second hand-written
    /// copy the research planners use. Two implementations of one type drift apart - that is the whole
    /// bug class - so checking only the shipped one leaves the other free to rot.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- clonefidelity
    /// </summary>
    public static class CloneFidelityTest
    {
        private static int passed, failed;

        public static int Run()
        {
            Console.WriteLine("=== GameClone: does every GameState field survive a clone? ===");
            var cloners = new (string Name, Func<GameState, GameState> Clone)[]
            {
                ("shipped",  ShippedClone.Clone),
                ("research", s => ResearchClone.Clone(s)),
            };
            foreach (var c in cloners)
            {
                Console.WriteLine($"  -- {c.Name} clone --");
                DeferredRemovalsSurvive(c.Name, c.Clone);
                EveryPopulatableFieldSurvives(c.Name, c.Clone);
                NestedObjectsSurviveToo(c.Name, c.Clone);
            }
            Console.WriteLine($"clonefidelity: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        private static GameState Fresh(string seed) => GameEngine.CreateMatch(new MatchConfig
        { SouthDeck = "st01", NorthDeck = "st01", Seed = seed });

        /// <summary>The concrete case that prompted this. A postponed removal is the engine's record
        /// that a Character is only still alive because a protection question has not been answered -
        /// exactly the machinery this session added prompts to. Lose it and the search sees the victim
        /// alive with nothing owed for it.</summary>
        private static void DeferredRemovalsSurvive(string who, Func<GameState, GameState> cloner)
        {
            var st = Fresh("clone-fidelity");
            st.DeferredRemovals.Add(new DeferredRemoval
            {
                EffectId = "effect-99", VictimSeat = "south", VictimInstanceId = "south-victim-1",
                GuardInstanceId = "south-guard-1", Kind = DeferredRemovalKind.Ko, ByBattleKo = true,
            });

            int n = cloner(st).DeferredRemovals?.Count ?? 0;
            Check($"[{who}] a postponed removal survives the clone", n == 1,
                  $"clone has {n}, original has 1 — the search would see the victim alive with nothing owed");
        }

        /// <summary>Every public field of GameState, so the next field added is covered without anyone
        /// remembering to extend this.</summary>
        private static void EveryPopulatableFieldSurvives(string who, Func<GameState, GameState> cloner)
        {
            var st = Fresh("clone-fields");
            var dropped = new List<string>();
            var skipped = new List<string>();

            foreach (var f in typeof(GameState).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object before;
                try { before = Populate(f, st); }
                catch (Exception) { skipped.Add(f.Name); continue; }
                if (before == null) { skipped.Add(f.Name); continue; }

                GameState copy;
                try { copy = cloner(st); }
                catch (Exception ex) { dropped.Add($"{f.Name} (clone threw {ex.GetType().Name})"); continue; }

                if (!Survived(before, f.GetValue(copy))) dropped.Add(f.Name);
            }

            int total = typeof(GameState).GetFields(BindingFlags.Public | BindingFlags.Instance).Length;
            Console.WriteLine($"    fields checked   : {total - skipped.Count}");
            Console.WriteLine($"    fields unchecked : {skipped.Count}"
                              + (skipped.Count > 0 ? "  (" + string.Join(", ", skipped.Take(8)) + ")" : ""));
            foreach (var d in dropped) Console.WriteLine($"      DROPPED BY CLONE: {d}");

            Check($"[{who}] no populatable GameState field is dropped", dropped.Count == 0,
                  dropped.Count == 0 ? "" : string.Join(", ", dropped));
        }

        /// <summary>The top-level walk cannot see the hand-written helpers - CloneBattle, ClonePE - which
        /// fall behind their own types the same way. Three were absent there:
        /// BattleState.BlockerPowerBanMax and .BlockerCostBanMax (the search believed it could Blocker
        /// with cards the battle had excluded) and PendingEffect.PlayedPickIds.</summary>
        private static void NestedObjectsSurviveToo(string who, Func<GameState, GameState> cloner)
        {
            var st = Fresh("clone-nested");
            st.Battle = new BattleState { Id = "b1", Step = "counter", AttackerSeat = "south", TargetSeat = "north" };
            st.PendingEffects.Add(new PendingEffect { EffectId = "e1", Seat = "south", Text = "probe" });

            var dropped = new List<string>();
            Probe(typeof(BattleState), st.Battle, () => cloner(st).Battle, dropped, "Battle");
            Probe(typeof(PendingEffect), st.PendingEffects[0],
                  () => cloner(st).PendingEffects.FirstOrDefault(), dropped, "PendingEffect");

            foreach (var d in dropped) Console.WriteLine($"      DROPPED BY CLONE: {d}");
            Check($"[{who}] no nested field is dropped by its cloner", dropped.Count == 0,
                  dropped.Count == 0 ? "" : string.Join(", ", dropped));
        }

        private static void Probe(Type t, object live, Func<object> cloneOf, List<string> dropped, string label)
        {
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object expect;
                var ft = f.FieldType;
                try
                {
                    if (ft == typeof(int) || ft == typeof(int?)) { f.SetValue(live, 4242); expect = 4242; }
                    else if (ft == typeof(bool)) { f.SetValue(live, true); expect = true; }
                    else if (ft == typeof(string)) { f.SetValue(live, "probe"); expect = "probe"; }
                    else if (f.GetValue(live) is IList l && ft.IsGenericType
                             && ft.GetGenericArguments()[0] == typeof(string))
                    { l.Add("probe"); expect = l.Count; }
                    else continue;
                }
                catch (Exception) { continue; }

                var copy = cloneOf();
                if (copy == null) { dropped.Add($"{label} (whole object)"); return; }
                var got = f.GetValue(copy);
                bool ok = expect is int n
                    ? (got is int gi ? gi == n : (got is ICollection gc && gc.Count == n))
                    : Equals(expect, got);
                if (!ok) dropped.Add($"{label}.{f.Name}");
            }
        }

        /// <summary>Give the field a value the clone cannot reproduce by accident, and return a token
        /// describing what to look for afterwards. Collections get an ELEMENT, because an empty list
        /// survives a clone that drops the field entirely.</summary>
        private static object Populate(FieldInfo f, GameState st)
        {
            var t = f.FieldType;
            if (t == typeof(int)) { f.SetValue(st, 4242); return 4242; }
            if (t == typeof(bool)) { f.SetValue(st, true); return true; }
            if (t == typeof(string)) { f.SetValue(st, "clone-probe"); return "clone-probe"; }

            var cur = f.GetValue(st);
            if (cur is IList list)
            {
                var elem = t.IsGenericType ? t.GetGenericArguments()[0] : null;
                if (elem == null) return null;
                object item;
                try { item = elem == typeof(string) ? (object)"clone-probe" : Activator.CreateInstance(elem); }
                catch (Exception) { return null; }
                int n0 = list.Count;
                list.Add(item);
                return n0 + 1;
            }
            if (cur is IDictionary dict)
            {
                var args = t.GetGenericArguments();
                if (args.Length != 2 || args[0] != typeof(string)) return null;
                object val;
                try { val = args[1] == typeof(int) ? (object)7 : Activator.CreateInstance(args[1]); }
                catch (Exception) { return null; }
                int n0 = dict.Count;
                dict["clone-probe"] = val;
                return n0 + 1;
            }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(HashSet<>)
                && t.GetGenericArguments()[0] == typeof(string))
            {
                var add = t.GetMethod("Add");
                var count = t.GetProperty("Count");
                int n0 = (int)count.GetValue(cur);
                add.Invoke(cur, new object[] { "clone-probe" });
                return n0 + 1;
            }
            return null;
        }

        private static bool Survived(object before, object after)
        {
            if (before is int expected)
            {
                if (after is int i) return i == expected;
                if (after == null) return false;
                if (after is ICollection c) return c.Count == expected;
                var cp = after.GetType().GetProperty("Count");
                return cp != null && (int)cp.GetValue(after) == expected;
            }
            if (before is bool b) return after is bool ab && ab == b;
            if (before is string s) return (after as string) == s;
            return false;
        }
    }
}
