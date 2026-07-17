// ANALYZER FIXTURES — synthetic source exercising every mutation form the analyzer claims to detect,
// with an ASSERTION per form. Run: `dotnet run -- --test`.
//
// WHY THIS EXISTS: v1 and v2 were both shipped with confident completeness claims and both were blind —
// v1 to helper-parameter mutation (ShuffleInPlace/Shift/Pop = 0) and v2 to object initializers, which
// made GameClone.cs (THE leak that started this investigation) report ZERO rows. Printing counts is not
// a test. A form that stops being detected must FAIL THE BUILD, not quietly vanish from a CSV.
//
// Type names here deliberately mirror the engine's, because detection is keyed on ContainingType.Name.

using System;
using System.Collections.Generic;
using System.Linq;

namespace ZoneAudit
{
    public static class Fixtures
    {
        // Each case: a name, the source, and the (target, kind) rows it MUST produce.
        record Case(string Name, string Source, (string Target, string Kind)[] Expect);

        const string Types = @"
using System.Collections.Generic;
public class CardInstance { public string Zone; public string InstanceId; public string CardId; }
public class BattleState { public CardInstance RevealedLife; }
public class DeckLookState { public string SourceInstanceId; public List<CardInstance> Cards = new List<CardInstance>(); public List<CardInstance> Ordered = new List<CardInstance>(); }
public class PendingEffect { public string SourceInstanceId; public string SourceCardId; }
public class ChoiceState { public string SourceInstanceId; }
public class PlayerState {
  public List<CardInstance> Deck = new List<CardInstance>();
  public List<CardInstance> Hand = new List<CardInstance>();
  public List<CardInstance> Life = new List<CardInstance>();
  public List<CardInstance> Trash = new List<CardInstance>();
  public List<CardInstance> CharacterArea = new List<CardInstance>();
  public List<CardInstance> CostArea = new List<CardInstance>();
  public CardInstance Stage;
}
public class GameState { public BattleState Battle; public DeckLookState DeckLook; public CardInstance Selected; }
";

        static readonly Case[] Cases =
        {
            new("direct collection mutation",
                "public static class T { public static void M(PlayerState p) { p.Hand.Add(null); } }",
                new[] { ("Hand", "call .Add()") }),

            new("indexer assignment",
                "public static class T { public static void M(PlayerState p) { p.CharacterArea[0] = null; } }",
                new[] { ("CharacterArea", "indexer assignment") }),

            new("whole-field assignment",
                "public static class T { public static void M(PlayerState p) { p.Deck = new List<CardInstance>(); } }",
                new[] { ("Deck", "whole-field replacement") }),

            // ⚠ THE v2 BLIND SPOT — this is GameClone.ClonePlayer's shape.
            new("object-initializer assignment (GameClone shape)",
                "public static class T { public static PlayerState M(PlayerState p) => new PlayerState { Deck = p.Deck, Hand = p.Hand, Life = p.Life }; }",
                new[] { ("Deck", "object-initializer assignment"), ("Hand", "object-initializer assignment"), ("Life", "object-initializer assignment") }),

            new("later local assignment (not initializer)",
                "public static class T { public static void M(PlayerState p) { List<CardInstance> z; z = p.Deck; z.Add(null); } }",
                new[] { ("Deck", "call .Add()") }),

            new("alias of alias",
                "public static class T { public static void M(PlayerState p) { var a = p.Life; var b = a; b.Add(null); } }",
                new[] { ("Life", "call .Add()") }),

            new("helper mutating a bare parameter",
                "public static class T { static void H(List<CardInstance> l) { l.Add(null); } public static void M(PlayerState p) { H(p.Trash); } }",
                new[] { ("Trash", "helper H(arg0)") }),

            new("transitive helper call",
                "public static class T { static void H(List<CardInstance> l) { l.Add(null); } static void G(List<CardInstance> l) { H(l); } public static void M(PlayerState p) { G(p.Deck); } }",
                new[] { ("Deck", "helper G(arg0)") }),

            new("Shift/Pop shape (returns, mutates param)",
                "public static class T { static CardInstance Pop(List<CardInstance> l) { var c = l[l.Count-1]; l.RemoveAt(l.Count-1); return c; } public static void M(PlayerState p) { Pop(p.Life); } }",
                new[] { ("Life", "helper Pop(arg0)") }),

            new("DeckLook collection replacement",
                "public static class T { public static void M(GameState s) { s.DeckLook = new DeckLookState { Cards = new List<CardInstance>(), Ordered = new List<CardInstance>() }; } }",
                new[] { ("DeckLookState.Cards", "object-initializer assignment"), ("DeckLookState.Ordered", "object-initializer assignment") }),

            new("RevealedLife cloning",
                "public static class T { public static BattleState M(BattleState b) => new BattleState { RevealedLife = b.RevealedLife }; }",
                new[] { ("BattleState.RevealedLife", "object-initializer assignment") }),

            new("RevealedLife direct write",
                "public static class T { public static void M(GameState s, CardInstance c) { s.Battle.RevealedLife = c; } }",
                new[] { ("BattleState.RevealedLife", "privacy-bearing write") }),

            // ⛔ REMOVED: "privacy-bearing SourceInstanceId/SourceCardId" and "ChoiceState identity".
            // Those fields are NO LONGER this tool's job. Enumerating privacy carriers statically failed
            // four times running; reachability is now proven at RUNTIME (poison scan + paired-world
            // noninterference) with NO STATIC PRIVACY-FIELD WHITELIST — the reachability walk enumerates no
            // fields (the poison/decision fixtures still enumerate the threat model). A fixture asserting a
            // capability the tool has deliberately dropped is worse than no fixture.

            // A REAL ref/out test: the ONLY way Life is mutated here is through the ref parameter, so
            // this FAILS if the analyzer's ref/out summary logic is deleted. The previous version asserted
            // `p.Stage = x`, which is detected as a plain whole-field write — it would have passed with
            // ref/out support removed entirely, i.e. it tested nothing.
            new("ref parameter propagation",
                "public static class T { static void H(ref List<CardInstance> l) { l = new List<CardInstance>(); } public static void M(PlayerState p) { var z = p.Life; H(ref z); } }",
                new[] { ("Life", "helper H(arg0)") }),
        };

        public static int Run()
        {
            int pass = 0, fail = 0;
            Console.WriteLine("=== analyzer fixtures ===\n");

            foreach (var c in Cases)
            {
                var sources = new[] { ("Types.cs", Types), ("Case.cs", "using System.Collections.Generic;\n" + c.Source) };
                var sites = Analyzer.Analyze(sources, null, out var errors);

                if (errors.Count > 0)
                {
                    fail++;
                    Console.WriteLine($"  FAIL  {c.Name}\n          compile error: {errors[0]}");
                    continue;
                }

                var missing = c.Expect
                    .Where(e => !sites.Any(s => s.Target == e.Target && s.Kind == e.Kind))
                    .ToList();

                if (missing.Count == 0) { pass++; Console.WriteLine($"  pass  {c.Name}"); }
                else
                {
                    fail++;
                    Console.WriteLine($"  FAIL  {c.Name}");
                    foreach (var m in missing) Console.WriteLine($"          missing: {m.Target} / {m.Kind}");
                    Console.WriteLine($"          got: {(sites.Count == 0 ? "<nothing>" : string.Join(", ", sites.Select(s => $"{s.Target}/{s.Kind}")))}");
                }
            }

            Console.WriteLine($"\nfixtures: {pass} pass, {fail} fail");
            return fail == 0 ? 0 : 1;
        }
    }
}
