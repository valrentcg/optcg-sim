using System.Collections.Generic;

namespace OnePieceTcg.Engine.Puzzles
{
    // AUTO-GENERATED per-color, color-LEGAL card pools for the puzzle generator (Tools emit_palette).
    // Every role list holds only cards whose color includes the pool color, so a board built from one
    // color is legal for a leader of that color. Do not hand-edit; regenerate from the card DB.
    public static class PuzzlePalette
    {
        public sealed class ColorPool
        {
            public string[] Leaders;
            public string[] P3000, P4000, P5000, P6000;
            public (string id, int cost)[] Blockers;
            public string[] Ctr1000, Ctr2000;
            public (string id, int cost)[] Ev2000, Ev4000;
            public (string id, int playCost, int targetCap)[] RestRemoval, KoRemoval;
            public string[] DoubleAttack;                 // board attackers with [Double Attack] (power >= 5000)
            public (string id, int cost)[] Rush;          // [Rush] finishers (power >= 6000) to play from hand
            public string Life;
        }
        public static readonly string[] Colors = { "Red", "Green", "Blue", "Purple", "Black", "Yellow" };
        public static readonly Dictionary<string, ColorPool> ByColor = new Dictionary<string, ColorPool>
        {
            ["Red"] = new ColorPool
            {
                Leaders = new[] { "EB01-001", "EB03-001", "EB04-001", "OP01-001", "OP01-002", "OP01-003", "OP02-002", "OP03-001", "OP04-001", "OP05-001", "OP05-002", "OP06-001" },
                P3000 = new[] { "EB01-005", "OP01-010", "OP04-007", "P-015", "ST01-003" }, P4000 = new[] { "OP01-012", "ST01-009", "ST21-005" }, P5000 = new[] { "OP01-023", "OP03-007", "OP05-012", "OP06-008", "OP08-009" }, P6000 = new[] { "EB03-002", "OP01-018", "OP02-003", "OP03-006", "OP07-007" },
                Blockers = new (string, int)[] { ("OP01-019", 2), ("OP02-012", 2), ("OP03-010", 2), ("OP04-005", 1), ("OP05-013", 2) },
                Ctr1000 = new[] { "EB01-005", "EB02-001", "EB02-004", "OP01-010", "OP01-012" }, Ctr2000 = new[] { "EB03-002", "OP11-003", "OP11-017", "OP12-002", "OP12-010" },
                Ev2000 = new (string, int)[] { ("OP01-029", 1), ("OP07-018", 1) }, Ev4000 = new (string, int)[] { ("EB03-011", 1), ("OP14-018", 1), ("ST30-015", 1) },
                RestRemoval = new (string, int, int)[] {  }, KoRemoval = new (string, int, int)[] {  },
                DoubleAttack = new string[] { "P-028" }, Rush = new (string, int)[] { ("ST01-012", 5), ("ST21-014", 5) },
                Life = "EB01-002",
            },
            ["Green"] = new ColorPool
            {
                Leaders = new[] { "EB01-001", "EB02-010", "OP01-002", "OP01-003", "OP01-031", "OP02-025", "OP02-026", "OP03-021", "OP03-022", "OP04-019", "OP04-020", "OP05-022" },
                P3000 = new[] { "OP01-036", "OP01-037", "OP03-023", "OP08-027", "OP16-023" }, P4000 = new[] { "OP01-053", "OP02-033", "OP03-033", "OP03-035", "OP08-035" }, P5000 = new[] { "OP01-043", "OP02-028", "OP05-035", "OP06-031", "OP12-035" }, P6000 = new[] { "OP01-045", "OP02-043", "OP07-027", "OP09-038", "OP11-032" },
                Blockers = new (string, int)[] { ("EB01-017", 2), ("EB02-012", 1), ("OP01-039", 2), ("OP02-031", 1), ("OP03-031", 2) },
                Ctr1000 = new[] { "EB01-018", "OP01-036", "OP01-037", "OP01-043", "OP01-045" }, Ctr2000 = new[] { "OP03-033", "OP11-032", "OP11-033", "OP12-023", "OP12-032" },
                Ev2000 = new (string, int)[] { ("EB03-020", 1), ("OP01-057", 1), ("OP04-037", 2), ("OP06-038", 1) }, Ev4000 = new (string, int)[] { ("OP04-035", 2), ("OP05-038", 2), ("OP14-036", 1), ("ST02-016", 2) },
                RestRemoval = new (string, int, int)[] { ("EB01-015", 1, 2), ("EB02-011", 3, 5), ("OP01-033", 3, 4) }, KoRemoval = new (string, int, int)[] { ("OP12-029", 3, 2), ("P-072", 4, 4) },
                DoubleAttack = new string[] { "OP01-121" }, Rush = new (string, int)[] {  },
                Life = "EB03-016",
            },
            ["Blue"] = new ColorPool
            {
                Leaders = new[] { "EB01-021", "EB03-001", "OP01-060", "OP01-061", "OP01-062", "OP02-026", "OP02-049", "OP03-040", "OP04-001", "OP04-039", "OP04-040", "OP05-022" },
                P3000 = new[] { "OP01-082", "OP02-060", "OP03-052", "ST03-011", "ST12-009" }, P4000 = new[] { "OP01-076", "OP03-046", "ST03-006", "ST12-015", "ST22-008" }, P5000 = new[] { "EB01-025", "EB02-029", "OP01-081", "OP10-054", "ST03-002" }, P6000 = new[] { "OP01-066", "OP02-054", "OP08-048", "OP09-049", "OP11-045" },
                Blockers = new (string, int)[] { ("OP03-050", 2), ("OP05-052", 2), ("OP06-054", 2), ("OP09-054", 2), ("OP10-053", 1) },
                Ctr1000 = new[] { "EB01-025", "EB02-029", "OP01-065", "OP01-066", "OP01-076" }, Ctr2000 = new[] { "OP11-045", "OP11-053", "OP11-055", "OP12-049", "OP12-055" },
                Ev2000 = new (string, int)[] { ("OP11-059", 1) }, Ev4000 = new (string, int)[] { ("OP01-086", 2), ("OP04-057", 2), ("OP07-055", 2), ("OP07-056", 1) },
                RestRemoval = new (string, int, int)[] { ("OP10-044", 1, 1), ("OP10-048", 3, 1), ("OP10-056", 2, 4) }, KoRemoval = new (string, int, int)[] {  },
                DoubleAttack = new string[] { "EB04-023", "OP09-047", "ST22-003" }, Rush = new (string, int)[] {  },
                Life = "EB01-023",
            },
            ["Purple"] = new ColorPool
            {
                Leaders = new[] { "EB01-021", "EB02-010", "OP01-061", "OP01-062", "OP01-091", "OP02-071", "OP02-072", "OP03-058", "OP04-019", "OP04-058", "OP05-060", "OP06-001" },
                P3000 = new[] { "OP01-104", "OP02-075", "OP02-084", "OP07-067", "ST04-009" }, P4000 = new[] { "OP02-080", "OP03-061", "ST04-007", "ST05-015" }, P5000 = new[] { "OP02-077", "OP12-067", "ST04-013", "ST05-012" }, P6000 = new[] { "EB02-034", "EB03-030", "OP01-103", "OP02-088", "OP09-063" },
                Blockers = new (string, int)[] { ("EB02-033", 1), ("EB04-034", 2), ("OP01-100", 2), ("OP02-074", 1), ("OP02-081", 2) },
                Ctr1000 = new[] { "EB01-032", "EB02-034", "OP01-092", "OP01-103", "OP01-104" }, Ctr2000 = new[] { "EB03-030", "OP02-075", "OP11-068", "OP11-078", "OP12-068" },
                Ev2000 = new (string, int)[] {  }, Ev4000 = new (string, int)[] { ("OP01-119", 2), ("OP10-080", 3), ("ST05-017", 2) },
                RestRemoval = new (string, int, int)[] { ("OP06-075", 2, 2), ("OP11-063", 2, 3), ("OP13-061", 3, 1) }, KoRemoval = new (string, int, int)[] { ("EB03-036", 4, 3), ("OP02-076", 4, 1), ("OP05-063", 4, 3) },
                DoubleAttack = new string[] { "OP02-087" }, Rush = new (string, int)[] {  },
                Life = "EB01-036",
            },
            ["Black"] = new ColorPool
            {
                Leaders = new[] { "EB01-040", "OP02-002", "OP02-072", "OP02-093", "OP03-076", "OP03-077", "OP04-020", "OP04-039", "OP05-001", "OP05-041", "OP06-021", "OP06-080" },
                P3000 = new[] { "OP02-097", "OP02-104", "ST06-003" }, P4000 = new[] { "OP02-107", "OP03-084", "OP05-083", "OP13-085", "ST06-009" }, P5000 = new[] { "OP02-116", "OP03-087", "OP09-094", "ST06-013", "ST08-011" }, P6000 = new[] { "EB03-040", "OP02-109", "OP03-082", "OP06-094", "OP08-078" },
                Blockers = new (string, int)[] { ("EB03-048", 2), ("EB04-046", 2), ("OP02-108", 2), ("OP04-077", 2), ("OP05-085", 2) },
                Ctr1000 = new[] { "EB01-041", "EB02-042", "EB02-043", "OP02-097", "OP02-104" }, Ctr2000 = new[] { "EB03-040", "OP11-087", "OP11-093", "OP12-083", "OP12-092" },
                Ev2000 = new (string, int)[] { ("OP04-095", 1), ("OP07-094", 1), ("OP07-095", 2), ("OP12-098", 1) }, Ev4000 = new (string, int)[] { ("OP07-095", 2) },
                RestRemoval = new (string, int, int)[] { ("OP04-082", 3, 1), ("OP04-091", 1, 1) }, KoRemoval = new (string, int, int)[] { ("OP02-098", 3, 3), ("OP03-093", 2, 1), ("OP04-082", 3, 1) },
                DoubleAttack = new string[] {  }, Rush = new (string, int)[] {  },
                Life = "EB01-047",
            },
            ["Yellow"] = new ColorPool
            {
                Leaders = new[] { "EB01-040", "EB04-001", "OP03-022", "OP03-077", "OP03-099", "OP04-040", "OP04-058", "OP05-002", "OP05-098", "OP06-022", "OP07-097", "OP08-058" },
                P3000 = new[] { "OP03-101", "OP04-100", "OP04-113", "OP07-103", "ST07-002" }, P4000 = new[] { "OP03-103", "OP07-104", "OP08-113", "OP12-111", "OP15-103" }, P5000 = new[] { "EB02-055", "OP03-100", "OP03-111", "OP05-105", "OP05-110" }, P6000 = new[] { "OP03-106", "OP07-102", "OP07-108", "OP07-113", "OP08-108" },
                Blockers = new (string, int)[] { ("EB01-052", 2), ("EB01-057", 2), ("EB04-053", 2), ("EB04-056", 1), ("EB04-057", 2) },
                Ctr1000 = new[] { "EB01-055", "OP03-101", "OP03-103", "OP03-106", "OP03-111" }, Ctr2000 = new[] { "EB02-055", "OP04-100", "OP05-105", "OP07-099", "OP07-107" },
                Ev2000 = new (string, int)[] { ("OP09-116", 1), ("OP12-115", 1), ("OP14-116", 4), ("ST13-018", 1) }, Ev4000 = new (string, int)[] { ("OP08-116", 2), ("OP10-115", 2), ("OP11-115", 1) },
                RestRemoval = new (string, int, int)[] { ("EB01-054", 3, 3), ("OP11-110", 3, 1) }, KoRemoval = new (string, int, int)[] { ("EB01-054", 3, 3), ("EB03-051", 3, 2), ("EB03-056", 4, 3) },
                DoubleAttack = new string[] {  }, Rush = new (string, int)[] {  },
                Life = "EB02-052",
            },
        };
    }
}