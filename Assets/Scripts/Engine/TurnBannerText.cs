using System;

/// <summary>
/// Builds the turn-banner headline from a player's display name.
/// UnityEngine-free on purpose so the headless harness can gate it — the string rules
/// here (role words, placeholders, possessives, length) are exactly the part that breaks
/// silently, and a broken headline is visible on every single turn of every match.
/// </summary>
public static class TurnBannerText
{
    /// <summary>
    /// Names that are really ROLE WORDS or placeholders, not player names. Puzzle and
    /// Restore set the display name to the literal "You"/"Opponent", which would otherwise
    /// build the broken "YOU'S TURN"; and "Player 2" tells the player strictly less than
    /// "OPPONENT'S TURN" does. "South"/"North" are engine seat identifiers and must never
    /// reach the UI at all.
    /// </summary>
    private static readonly string[] NonNames =
        { "You", "Opponent", "Player 1", "Player 2", "South", "North" };

    /// <summary>Longest name rendered before it is clipped, so best-fit never has to
    /// shrink the headline past legibility. Usernames are arbitrary and not ours.</summary>
    public const int MaxNameLength = 20;

    /// <param name="displayName">The render-layer display name of whoever holds the turn.</param>
    /// <param name="mine">True when that player is the local player.</param>
    public static string Label(string displayName, bool mine)
    {
        string role = mine ? "YOUR TURN" : "OPPONENT'S TURN";
        if (string.IsNullOrEmpty(displayName)) return role;
        string n = displayName.Trim();
        if (n.Length == 0) return role;

        for (int i = 0; i < NonNames.Length; i++)
            if (n.Equals(NonNames[i], StringComparison.OrdinalIgnoreCase)) return role;

        if (n.Length > MaxNameLength) n = n.Substring(0, MaxNameLength - 1).TrimEnd() + "...";

        // ASCII apostrophe on purpose: the typographic U+2019 is not guaranteed to be in
        // titleFont's atlas, and a missing glyph renders as tofu.
        return n.ToUpperInvariant() + "'S TURN";
    }
}
