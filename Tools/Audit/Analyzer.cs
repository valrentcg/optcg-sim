// The analyzer core, separated from the driver so ANALYZER FIXTURES (Fixtures.cs) can run it against
// synthetic source and ASSERT each mutation form is detected.
//
// ── WHY v3 EXISTS ────────────────────────────────────────────────────────────────────────────────────
// v2 reported ZERO rows from GameClone.cs — the file whose verbatim Deck/Hand/Life copy IS the leak this
// whole investigation exists to fix. Cause: v2 recognized a privacy write only when
// AssignmentExpression.Left was MemberAccessExpression (`x.Field = …`). OBJECT INITIALIZERS
// (`new PlayerState { Deck = …, Hand = … }`) have a BARE IDENTIFIER on the left, so every clone copy,
// and every DeckLookState/PendingEffect/ChoiceState initializer in GameEngine, was invisible.
//
// v2 also only PRINTED its regression probes. It could regress to zero and still exit 0. v3 asserts.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZoneAudit
{
    public record Site(string File, int Line, string Target, string Kind, string Via, string Snippet);

    public static class Analyzer
    {
        public static readonly HashSet<string> ZoneFields = new()
        { "Deck", "Hand", "Life", "Trash", "CharacterArea", "Stage", "CostArea" };

        // ── SCOPE: KNOWLEDGE-CHANGING TRANSITIONS ONLY ───────────────────────────────────────────────
        // NOT a privacy whitelist. v1-v3 tried to enumerate every privacy-bearing field and were blind
        // four times running (helper params → object initializers → CommandHistory/EventLog/CardModifier/
        // ChoiceState.SourceCardId). A static analyzer gated on a hand-maintained list of secrets is
        // structurally always one field behind. THE PRIVACY GATE IS THE RUNTIME TEST SUITE, not this file:
        //   (1) structural poison scan  (2) paired-world noninterference  (3) a TYPED planner boundary.
        // This tool's ONLY job is to find knowledge-changing operations to migrate onto the ledger funnel.
        //
        // ⚠ These are TRANSITION CANDIDATES, never a proven-complete inventory. Completeness becomes
        // durable only AFTER migration, by making the zone-mutation surface private so a bypass fails
        // COMPILATION — not by this analyzer getting cleverer.
        public static readonly (string Type, string Member)[] TransitionCarriers =
        {
            ("BattleState",   "RevealedLife"),   // the life card in flight (private to the defender)
            ("DeckLookState", "Cards"),          // look/search lifecycle
            ("DeckLookState", "Ordered"),
            ("GameState",     "DeckLook"),       // creation + destruction (order is destroyed at :1896)
            ("CardInstance",  "Zone"),           // movement
            ("CardInstance",  "FaceUp"),         // reveal / conceal
        };

        public static readonly HashSet<string> Mutators = new()
        { "Add", "AddRange", "Insert", "InsertRange", "Remove", "RemoveAt", "RemoveAll", "RemoveRange",
          "Clear", "Sort", "Reverse" };

        /// <summary>SOURCE SCOPING (not privacy classification): parse every engine file so symbols and
        /// call-graph summaries resolve, but EMIT transition candidates only from the AUTHORITATIVE
        /// MUTATION LAYER. A GameClone copy is an implementation detail of cloning, not a semantic game
        /// transition; classifying it would put clone plumbing in the transition table.
        /// NOT a return to filename-based privacy: Tools/Audit audits authoritative TRANSITIONS, while
        /// PrivacyTest audits EVERY object passed to planning by RUNTIME reachability, with no
        /// enumeration at all.</summary>
        public static readonly string[] AuthoritativeMutationLayer = { "GameEngine.cs" };

        // ⛔ REMOVED: `ProjectionFiles` + the A/B inventory split.
        // Assigning "privacy reachability" from the FILENAME was architecturally wrong: privacy relevance
        // and gameplay-transition are OVERLAPPING properties of a SITE, not mutually exclusive buckets of
        // a file. It mislabeled every GameEngine write to PendingEffect/ChoiceState/Selected as a "zone
        // transition", so "A = 334 gameplay movements" was false. Reachability is now answered at RUNTIME
        // (poison scan + paired-world noninterference), where no enumeration is required.

        public static List<Site> Analyze(IEnumerable<(string Path, string Text)> sources, string repoForRel, out List<string> errors)
        {
            var trees = sources.ToDictionary(s => s.Path, s => CSharpSyntaxTree.ParseText(s.Text, path: s.Path));
            var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator).Where(p => p.EndsWith(".dll"))
                .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));

            var comp = CSharpCompilation.Create("EngineAudit", trees.Values, refs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            errors = comp.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => $"{d.Id} {d.Location.GetLineSpan()}: {d.GetMessage()}").ToList();

            var models = trees.ToDictionary(kv => kv.Key, kv => comp.GetSemanticModel(kv.Value));
            var sites = new List<Site>();

            // ── aliases: initializers AND later assignments, cheap fixpoint for alias-of-alias ────────
            var aliases = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
            for (int round = 0; round < 3; round++)
                foreach (var (path, tree) in trees)
                {
                    var model = models[path];
                    foreach (var d in tree.GetRoot().DescendantNodes().OfType<VariableDeclaratorSyntax>())
                        if (d.Initializer?.Value != null && ZoneOf(d.Initializer.Value, model, aliases) is string z1
                            && model.GetDeclaredSymbol(d) is ISymbol s1) aliases[s1] = z1;

                    foreach (var a in tree.GetRoot().DescendantNodes().OfType<AssignmentExpressionSyntax>())
                        if (a.Left is IdentifierNameSyntax lid
                            && model.GetSymbolInfo(lid).Symbol is ILocalSymbol ls
                            && ZoneOf(a.Right, model, aliases) is string z2) aliases[ls] = z2;
                }

            // ── interprocedural mutation summaries ────────────────────────────────────────────────────
            var summary = new Dictionary<IMethodSymbol, HashSet<int>>(SymbolEqualityComparer.Default);
            var decls = new List<(MethodDeclarationSyntax D, IMethodSymbol S, SemanticModel M)>();
            foreach (var (path, tree) in trees)
                foreach (var m in tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())
                    if (models[path].GetDeclaredSymbol(m) is IMethodSymbol ms) decls.Add((m, ms, models[path]));

            bool changed = true;
            for (int iter = 0; iter < 12 && changed; iter++)
            {
                changed = false;
                foreach (var (decl, sym, model) in decls)
                {
                    if (!summary.TryGetValue(sym, out var set)) summary[sym] = set = new HashSet<int>();
                    foreach (var node in decl.DescendantNodes())
                    {
                        ExpressionSyntax recv = node switch
                        {
                            InvocationExpressionSyntax i when i.Expression is MemberAccessExpressionSyntax ma
                                && Mutators.Contains(ma.Name.Identifier.Text) => ma.Expression,
                            AssignmentExpressionSyntax a when a.Left is ElementAccessExpressionSyntax ea => ea.Expression,
                            _ => null,
                        };
                        if (recv is IdentifierNameSyntax rid && model.GetSymbolInfo(rid).Symbol is IParameterSymbol ps
                            && SymbolEqualityComparer.Default.Equals(ps.ContainingSymbol, sym) && set.Add(ps.Ordinal)) changed = true;

                        if (node is InvocationExpressionSyntax call
                            && model.GetSymbolInfo(call).Symbol is IMethodSymbol callee
                            && summary.TryGetValue(callee, out var cset))
                        {
                            var argl = call.ArgumentList.Arguments;
                            for (int i = 0; i < argl.Count; i++)
                                if (cset.Contains(i) && argl[i].Expression is IdentifierNameSyntax aid
                                    && model.GetSymbolInfo(aid).Symbol is IParameterSymbol aps
                                    && SymbolEqualityComparer.Default.Equals(aps.ContainingSymbol, sym)
                                    && set.Add(aps.Ordinal)) changed = true;
                        }

                        if (node is ArgumentSyntax arg
                            && arg.RefKindKeyword.Kind() is SyntaxKind.RefKeyword or SyntaxKind.OutKeyword
                            && arg.Expression is IdentifierNameSyntax refId
                            && model.GetSymbolInfo(refId).Symbol is IParameterSymbol rps
                            && SymbolEqualityComparer.Default.Equals(rps.ContainingSymbol, sym)
                            && set.Add(rps.Ordinal)) changed = true;

                        // REASSIGNMENT of a ref/out parameter: `l = new List<CardInstance>()`. This is the
                        // PRIMARY ref case and it was missing — the old ref/out fixture asserted
                        // `p.Stage = x`, a plain whole-field write, so it passed while this gap sat open.
                        // An honest fixture found it immediately.
                        if (node is AssignmentExpressionSyntax pasg && pasg.Left is IdentifierNameSyntax pid
                            && model.GetSymbolInfo(pid).Symbol is IParameterSymbol pps
                            && pps.RefKind is RefKind.Ref or RefKind.Out
                            && SymbolEqualityComparer.Default.Equals(pps.ContainingSymbol, sym)
                            && set.Add(pps.Ordinal)) changed = true;
                    }
                }
            }
            Summaries = summary;

            // ── sites ─────────────────────────────────────────────────────────────────────────────────
            foreach (var (path, tree) in trees)
            {
                var model = models[path];
                // Emit only from the authoritative layer; other files still contributed symbols above.
                // repoForRel == null means fixture mode, where synthetic sources must always emit.
                if (repoForRel != null
                    && !AuthoritativeMutationLayer.Any(f => path.EndsWith(f, StringComparison.OrdinalIgnoreCase)))
                    continue;

                foreach (var node in tree.GetRoot().DescendantNodes())
                {
                    switch (node)
                    {
                        case InvocationExpressionSyntax inv2:
                        {
                            if (inv2.Expression is MemberAccessExpressionSyntax ma && Mutators.Contains(ma.Name.Identifier.Text)
                                && ZoneOf(ma.Expression, model, aliases) is string z)
                            { Add(sites, repoForRel, path, node, z, $"call .{ma.Name.Identifier.Text}()", "direct"); break; }

                            if (model.GetSymbolInfo(inv2).Symbol is IMethodSymbol callee && summary.TryGetValue(callee, out var mset))
                            {
                                var argl = inv2.ArgumentList.Arguments;
                                for (int i = 0; i < argl.Count; i++)
                                    if (mset.Contains(i) && ZoneOf(argl[i].Expression, model, aliases) is string za)
                                        Add(sites, repoForRel, path, node, za, $"helper {callee.Name}(arg{i})", $"interprocedural via {callee.Name}");
                            }
                            break;
                        }
                        case AssignmentExpressionSyntax asg:
                        {
                            if (asg.Left is ElementAccessExpressionSyntax ea && ZoneOf(ea.Expression, model, aliases) is string zi)
                            { Add(sites, repoForRel, path, node, zi, "indexer assignment", "direct"); break; }

                            // ⚠ THE v2 BLIND SPOT: object-initializer assignments have a BARE IDENTIFIER
                            // on the left (`new PlayerState { Deck = … }`), not member access. Resolve the
                            // symbol either way — this is what makes GameClone.cs visible at all.
                            var leftSym = asg.Left switch
                            {
                                MemberAccessExpressionSyntax m => model.GetSymbolInfo(m).Symbol,
                                IdentifierNameSyntax id => model.GetSymbolInfo(id).Symbol,
                                _ => null,
                            };
                            if (leftSym is IFieldSymbol or IPropertySymbol)
                            {
                                string mem = leftSym.Name, owner = leftSym.ContainingType?.Name;
                                bool inInit = asg.Parent is InitializerExpressionSyntax;
                                string kind = inInit ? "object-initializer assignment" : "whole-field replacement";

                                if (ZoneFields.Contains(mem) && owner is "PlayerState" or "GameState")
                                { Add(sites, repoForRel, path, node, mem, kind, "direct"); break; }

                                var pb = TransitionCarriers.FirstOrDefault(p => p.Member == mem && p.Type == owner);
                                if (pb.Member != null)
                                { Add(sites, repoForRel, path, node, $"{pb.Type}.{pb.Member}", inInit ? "object-initializer assignment" : "privacy-bearing write", "direct"); break; }

                                if (mem == "Zone")
                                { Add(sites, repoForRel, path, node, "(Zone string)", "zone-string assignment", "direct"); break; }
                            }
                            break;
                        }
                    }
                }
            }
            return sites;
        }

        public static Dictionary<IMethodSymbol, HashSet<int>> Summaries = new(SymbolEqualityComparer.Default);

        static bool IsOwner(ISymbol s, params string[] types) =>
            s is IFieldSymbol or IPropertySymbol && types.Contains(s.ContainingType?.Name);

        static string ZoneOf(ExpressionSyntax e, SemanticModel model, Dictionary<ISymbol, string> aliases)
        {
            for (int d = 0; d < 8 && e != null; d++)
                switch (e)
                {
                    case ParenthesizedExpressionSyntax p: e = p.Expression; continue;
                    case CastExpressionSyntax c: e = c.Expression; continue;
                    case ConditionalAccessExpressionSyntax ca: e = ca.Expression; continue;
                    case MemberAccessExpressionSyntax ma:
                    {
                        var n = ma.Name.Identifier.Text;
                        var sym = model.GetSymbolInfo(ma).Symbol;
                        if (ZoneFields.Contains(n) && IsOwner(sym, "PlayerState", "GameState")) return n;
                        var pb = TransitionCarriers.FirstOrDefault(p => p.Member == n && IsOwner(sym, p.Type));
                        return pb.Member != null ? $"{pb.Type}.{pb.Member}" : null;
                    }
                    case IdentifierNameSyntax id:
                    {
                        var sym = model.GetSymbolInfo(id).Symbol;
                        if (sym != null && aliases.TryGetValue(sym, out var z)) return z;
                        if (sym is IFieldSymbol or IPropertySymbol)
                        {
                            if (ZoneFields.Contains(sym.Name) && IsOwner(sym, "PlayerState", "GameState")) return sym.Name;
                            var pb = TransitionCarriers.FirstOrDefault(p => p.Member == sym.Name && IsOwner(sym, p.Type));
                            if (pb.Member != null) return $"{pb.Type}.{pb.Member}";
                        }
                        return null;
                    }
                    default: return null;
                }
            return null;
        }

        static void Add(List<Site> sites, string repo, string path, SyntaxNode node, string target, string kind, string via)
        {
            int line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var text = node.ToString().Replace("\r", " ").Replace("\n", " ");
            if (text.Length > 90) text = text.Substring(0, 90) + "…";
            string rel = repo != null && File.Exists(path) ? Path.GetRelativePath(repo, path).Replace('\\', '/') : Path.GetFileName(path);
            sites.Add(new Site(rel, line, target, kind, via, text));
        }
    }
}
