using System.Collections.Generic;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim.Runner
{
    /// <summary>
    /// Reconstruct the authoritative <see cref="GameCommand"/> for a root <see cref="ObservedAction"/> INSIDE
    /// a specific world, using that world's correspondences: surrogate→instance (cards) and pendingRef→EffectId
    /// (effects). This is the honest inverse of <see cref="ObservedSeam"/>'s descriptor: it maps each opaque
    /// handle to the exact id IN THIS WORLD, per command TYPE, so an attacker lands in <c>Attacker</c>, a
    /// blocker in <c>Blocker</c>, an effect gets its <c>EffectId</c>, etc.
    ///
    /// FAILS CLOSED: returns null if ANY named reference (actor, target, ordered card, or pending effect) is
    /// unresolved in the given correspondence, or the type is unknown — never a command with a null id in a
    /// field the engine will read.
    /// </summary>
    public static class RootActionTranslator
    {
        public static GameCommand ToWorldCommand(ObservedAction a, string seat,
            IReadOnlyDictionary<string, string> surrogateToInstance,
            IReadOnlyDictionary<string, string> pendingRefToEffectId)
        {
            if (a == null) return null;
            bool ok = true;
            string Inst(string sur)
            {
                if (sur == null) return null;
                if (surrogateToInstance.TryGetValue(sur, out var id)) return id;
                ok = false; return null;
            }
            string Eff(string r)
            {
                if (r == null) { ok = false; return null; }   // an effect action with no ref cannot be addressed
                if (pendingRefToEffectId.TryGetValue(r, out var e)) return e;
                ok = false; return null;
            }

            // A REQUIRED reference/scalar that is absent ⇒ fail closed. (Inst/Eff already flip ok on an
            // unresolved id; these guards catch a MISSING handle the descriptor should have carried.)
            void Require(bool present) { if (!present) ok = false; }

            var cmd = new GameCommand { Type = a.Type, Seat = seat };
            switch (a.Type)
            {
                case "declareAttack":
                    Require(a.ActorSurrogate != null && a.TargetSurrogate != null);
                    cmd.Attacker = Inst(a.ActorSurrogate);
                    cmd.Target = Inst(a.TargetSurrogate);
                    break;
                case "blockAttack":
                    Require(a.ActorSurrogate != null);
                    cmd.Blocker = Inst(a.ActorSurrogate);
                    break;
                case "playCard":
                case "counterWithCard":
                    Require(a.ActorSurrogate != null);
                    cmd.InstanceId = Inst(a.ActorSurrogate);
                    break;
                case "activateMain":
                    Require(a.ActorSurrogate != null);
                    cmd.Target = Inst(a.ActorSurrogate);              // engine ActivateMain reads Target
                    break;
                case "attachDon":
                    Require(a.TargetSurrogate != null && a.Amount.HasValue);
                    cmd.Target = Inst(a.TargetSurrogate);
                    cmd.Amount = a.Amount;
                    break;
                case "resolveChoice":
                    Require(a.ChoiceOption != null);
                    cmd.Target = a.ChoiceOption;                       // "A" / "B"
                    break;
                case "deckLookSelect":
                    // The ONE intentional empty target: a decline carries no surrogate ⇒ Target "".
                    cmd.Target = a.TargetSurrogate != null ? Inst(a.TargetSurrogate) : "";
                    break;
                case "deckLookConfirmOrder":
                case "deckLookScryConfirm":
                    Require(a.OrderedSurrogates != null);             // an order action MUST carry an order
                    cmd.OrderedInstanceIds = ResolveOrder(a.OrderedSurrogates, surrogateToInstance, ref ok);
                    break;
                case "resolveEffect":
                    cmd.EffectId = Eff(a.PendingRef);                 // Eff flips ok if the ref is missing/unknown
                    // The other intentional empty target: an empty selection carries no surrogate ⇒ Target "".
                    cmd.Target = a.TargetSurrogate != null ? Inst(a.TargetSurrogate) : "";
                    break;
                case "passEffect":
                    cmd.EffectId = Eff(a.PendingRef);
                    break;
                case "chooseTurnOrder":
                    Require(a.GoingFirst.HasValue);
                    cmd.GoingFirst = a.GoingFirst;
                    break;
                case "mulliganDecision":
                    Require(a.Mulligan.HasValue);
                    cmd.Mulligan = a.Mulligan;
                    break;
                // Reference-free commands.
                case "endTurn":
                case "passBlock":
                case "passCounter":
                case "passTrigger":
                case "useTrigger":
                case "resolveAttack":
                    break;
                default:
                    ok = false;   // unknown type ⇒ fail closed rather than emit a partial command
                    break;
            }
            return ok ? cmd : null;
        }

        private static List<string> ResolveOrder(IReadOnlyList<string> orderedSurrogates,
            IReadOnlyDictionary<string, string> surrogateToInstance, ref bool ok)
        {
            if (orderedSurrogates == null) return null;
            var ids = new List<string>(orderedSurrogates.Count);
            foreach (var s in orderedSurrogates)
            {
                if (s == null || !surrogateToInstance.TryGetValue(s, out var id)) { ok = false; return ids; }
                ids.Add(id);
            }
            return ids;
        }
    }
}
