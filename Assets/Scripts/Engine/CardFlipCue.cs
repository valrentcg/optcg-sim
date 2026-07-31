using System.Collections.Generic;

/// <summary>
/// Decides when the "card flipped face-up" sound should fire.
///
/// UnityEngine-free on purpose. This logic shipped broken TWICE — first keyed only on the
/// Trigger-step reveal (so an in-place flip was never seen at all), then behind an early
/// return that made the in-place scan unreachable whenever there was no battle, which is
/// exactly when Wyper and Shirahoshi flip. Both were invisible to every existing gate
/// because the whole thing lived inside a MonoBehaviour. It lives here so the harness can
/// prove the cue actually fires.
///
/// Three separate events all read to the player as "a life card flipped":
///   1. IN PLACE   — OP11-022 Shirahoshi / OP15-114 Wyper turn a Life card face-up; the card
///                   never moves and there is no battle.
///   2. REVEALED   — the Trigger step lifts the top Life card out of the zone entirely.
///   3. LEFT LIFE  — damage taken to hand, an effect trashing Life, or one adding the top
///                   Life card to hand.
/// They overlap: a Trigger-step card is revealed and then LEAVES, so without shared de-dupe
/// one card would sound twice.
/// </summary>
public sealed class CardFlipCue
{
    private readonly HashSet<string> faceUpNow = new HashSet<string>();
    private readonly HashSet<string> sounded = new HashSet<string>();
    private string lastRevealId;
    private bool primed;

    public void Reset()
    {
        faceUpNow.Clear();
        sounded.Clear();
        lastRevealId = null;
        primed = false;
    }

    /// <summary>
    /// Call once per render with every currently face-up Life card id, plus the id of the card
    /// revealed for the Trigger step (null when no battle). Returns how many flip cues to play.
    /// The FIRST call after a reset primes silently: restoring a save or joining a match in
    /// progress can present several already-face-up Life cards, and none of those were "just
    /// flipped".
    /// </summary>
    public int Observe(ICollection<string> currentFaceUpIds, string revealedId)
    {
        int cues = 0;

        // 1. Trigger-step reveal. Compared against the previous id rather than "is anything
        //    revealed", so a repaint during the step does not retrigger, while [Double Attack]
        //    revealing a SECOND card still counts as its own flip.
        if (revealedId != lastRevealId)
        {
            lastRevealId = revealedId;
            if (!string.IsNullOrEmpty(revealedId) && sounded.Add(revealedId)) cues++;
        }

        // 2. In-place face-up flips.
        if (currentFaceUpIds != null)
        {
            foreach (var id in currentFaceUpIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (faceUpNow.Add(id) && primed) cues++;
            }
            // Forget cards that are face-DOWN again, so turning one back up later sounds again.
            faceUpNow.IntersectWith(currentFaceUpIds);
        }
        else faceUpNow.Clear();

        primed = true;
        return cues;
    }

    /// <summary>
    /// A card LEAVING the Life zone (damage to hand, trashed by an effect, added to hand).
    /// Returns true at most once per card, and never for one already sounded as a Trigger-step
    /// reveal — that card is revealed first and leaves a moment later.
    /// </summary>
    public bool ShouldSoundLeavingLife(string instanceId)
    {
        return !string.IsNullOrEmpty(instanceId) && sounded.Add(instanceId);
    }

    /// <summary>Records an id as already sounded without producing a cue. For the in-place path,
    /// so a card flipped face-up and later moved does not sound a second time on the way out.</summary>
    public void MarkSounded(string instanceId)
    {
        if (!string.IsNullOrEmpty(instanceId)) sounded.Add(instanceId);
    }
}
