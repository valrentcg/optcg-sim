using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OnePieceTcg.Engine;
using OnePieceTcg.Engine.Bot.Search;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// GameClone.Clone is a HAND-WRITTEN deep copy, so every field added to GameState has to be
    /// remembered there too. That is the same drift shape as the reveal-cost bug - a second
    /// implementation of a rule falling behind the first - except the consequence is worse: the bot
    /// searches on clones and rewind resimulates from them, so a dropped field means the AI is
    /// planning against a board that differs from the real one, silently.
    ///
    /// Checking by eye does not scale and does not survive the next field. This walks GameState's
    /// public fields by REFLECTION, gives each a value distinguishable from its default, clones, and
    /// asserts the value survived. Fields it cannot populate generically are reported as unchecked
    /// rather than silently skipped - an unchecked field is not a passing field.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- clonefidelity
    /// </summary>
    public static class CloneFidelityTest
    {
        private static int passed, failed;

        public static int Run()
        {
            Console.WriteLine("=== GameClone: does every GameState field survive a clone? ===");
            DeferredRemovalsSurvive();
            EveryPopulatableFieldSurvives();
            NestedObjectsSurviveToo();
            Console.WriteLine($"clonefidelity: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void Check(string label, bool ok, string detail = "")
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + label); }
            else { failed++; Console.WriteLine("  FAIL  " + label + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        /// <summary>The concrete case that prompted this. A postponed removal is the engine's record
        /// that a Character is only still alive because a protection question has not been answered.
        /// Lose it in the clone and the searching bot sees a board where the victim survives for free.
        /// </summary>
        private static void DeferredRemovalsSurvive()
        {
            var st = GameEngine.CreateMatch(new MatchConfig
            { SouthDeck = "st01", NorthDeck = "st01", Seed = "clone-fidelity" });
            st.DeferredRemovals.Add(new DeferredRemoval
            {
                EffectId = "effect-99",
                VictimSeat = "south",
                VictimInstanceId = "south-victim-1",
                GuardInstanceId = "south-guard-1",
                Kind = DeferredRemovalKind.Ko,
                ByBattleKo = true,
            });

            var clone = GameClone.Clone(st);
            int n = clone.DeferredRemovals?.Count ?? 0;
            Check("a postponed removal survives the clone", n == 1,
                  $"clone has {n} deferred removal(s), original has 1 — the bot would search a board "
                  + "where the victim is alive with nothing owed");
        }

        /// <summary>Every public field of GameState, by reflection, so the next field added is covered
        /// without anyone remembering to extend this test.</summary>
        private static void EveryPopulatableFieldSurvives()
        {
            var st = GameEngine.CreateMatch(new MatchConfig
            { SouthDeck = "st01", NorthDeck = "st01", Seed = "clone-fields" });

            var dropped = new List<string>();
            var unchecked_ = new List<string>();

            foreach (var f in typeof(GameState).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                // Populated and compared per-kind. Scalars get a distinctive value; collections get an
                // element, since an empty list survives a clone that drops the field entirely.
                object before;
                try { before = Populate(f, st); }
                catch (Exception) { unchecked_.Add(f.Name); continue; }
                if (before == null) { unchecked_.Add(f.Name); continue; }

                GameState clone;
                try { clone = GameClone.Clone(st); }
                catch (Exception ex) { dropped.Add($"{f.Name} (clone threw {ex.GetType().Name})"); continue; }

                var after = f.GetValue(clone);
                if (!Survived(before, after)) dropped.Add(f.Name);
            }

            Console.WriteLine($"    fields checked   : {typeof(GameState).GetFields(BindingFlags.Public | BindingFlags.Instance).Length - unchecked_.Count}");
            Console.WriteLine($"    fields unchecked : {unchecked_.Count}"
                              + (unchecked_.Count > 0 ? "  (" + string.Join(", ", unchecked_.Take(8)) + ")" : ""));
            foreach (var d in dropped) Console.WriteLine($"      DROPPED BY CLONE: {d}");

            Check("no populatable GameState field is dropped by the clone", dropped.Count == 0,
                  dropped.Count == 0 ? "" : string.Join(", ", dropped));
        }

        /// <summary>The top-level walk misses the hand-written helpers - CloneBattle, ClonePE and
        /// friends - which fall behind their own types the same way. Three fields were absent
        /// there: BattleState.BlockerPowerBanMax and .BlockerCostBanMax (a searching bot believed
        /// it could Blocker with cards the battle had excluded) and PendingEffect.PlayedPickIds.
        /// Reflection again, so the next one is caught without anybody remembering.</summary>
        private static void NestedObjectsSurviveToo()
        {
            var st = GameEngine.CreateMatch(new MatchConfig
            { SouthDeck = "st01", NorthDeck = "st01", Seed = "clone-nested" });
            st.Battle = new BattleState { Id = "b1", Step = "counter", AttackerSeat = "south", TargetSeat = "north" };
            st.PendingEffects.Add(new PendingEffect { EffectId = "e1", Seat = "south", Text = "probe" });

            var dropped = new List<string>();
            Probe(typeof(BattleState), st.Battle, () => GameClone.Clone(st).Battle, dropped, "Battle");
            Probe(typeof(PendingEffect), st.PendingEffects[0],
                  () => GameClone.Clone(st).PendingEffects.FirstOrDefault(), dropped, "PendingEffect");

            foreach (var d in dropped) Console.WriteLine($"      DROPPED BY CLONE: {d}");
            Check("no nested field is dropped by its cloner", dropped.Count == 0,
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
                    if (ft == typeof(int)) { f.SetValue(live, 4242); expect = 4242; }
                    else if (ft == typeof(int?)) { f.SetValue(live, 4242); expect = 4242; }
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

        /// <summary>Give the field a value the clone cannot reproduce by accident, and hand back a
        /// token describing what to look for afterwards.</summary>
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
                return n0 + 1;                      // expect this many afterwards
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
            // HashSet<string> and similar: no non-generic interface, so reach it by name.
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(HashSet<>)
                && t.GetGenericArguments()[0] == typeof(string))
            {
                var add = t.GetMethod("Add");
                var count = t.GetProperty("Count");
                int n0 = (int)count.GetValue(cur);
                add.Invoke(cur, new object[] { "clone-probe" });
                return n0 + 1;
            }
            return null;   // unchecked
        }

        private static bool Survived(object before, object after)
        {
            if (before is int expectedCountOrValue)
            {
                if (after is int i) return i == expectedCountOrValue;
                if (after == null) return false;
                if (after is ICollection c) return c.Count == expectedCountOrValue;
                var cp = after.GetType().GetProperty("Count");
                if (cp != null) return (int)cp.GetValue(after) == expectedCountOrValue;
                return false;
            }
            if (before is bool b) return after is bool ab && ab == b;
            if (before is string s) return (after as string) == s;
            return false;
        }
    }
}
