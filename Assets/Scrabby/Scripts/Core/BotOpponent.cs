using UnityEngine;

// The opponents who turn up when nobody else does.
//
// Quick game promises a game straight away, and that promise is only as good
// as the number of people holding the phone at that moment - which, for a new
// game, is nobody. So after a short wait one of these takes the empty seat and
// plays the match out.
//
// Ten of them, each with a fixed name and a fixed strength, because a single
// bot wearing a different name every match is worse than no bot at all: the
// progress card files opponents by who they are, so ten games would collapse
// into one row that renamed itself every time. These are ten people as far as
// anything else in the game is concerned, and a name that comes back with a
// record behind it is what a small player base actually looks like.
//
// It is a real match in the database: a real match id, real rounds, real
// submissions, resolved the same way. The only thing that is not real is the
// player, and on Kingsley's decision the game does not say so. Everything that
// prints an opponent's name still goes through Label, so the marker is one
// line away if that is ever reconsidered.
//
// The moves are worked out on the waiting player's own device, by the same
// search the solo computer uses. Putting it on a server would mean the word
// engine, the dictionary, the board and the scoring all written a second time
// in another language, and paid for by the hour.
public static class BotOpponent
{
    // Not Firebase accounts, and not meant to be: no bot ever signs in. These
    // are names for a seat, all starting "bot-" so anything can recognise one
    // without a lookup.
    public const string UidPrefix = "bot-";

    public struct Player
    {
        public string uid;
        public string name;
        public GameLogic.SoloDifficulty level;

        // How long after the player's move the answer lands, in minutes. Not
        // seconds: a reply that arrives while you are still looking at the
        // board is the one thing no human opponent ever does.
        public float slowestMinutes;
        public float quickestMinutes;
    }

    // Two easy, three medium, three hard, two expert - and a spread of
    // rhythms, because ten opponents who all answer in the same ten minutes
    // are as much of a tell as ten who all play equally well.
    private static readonly Player[] Everyone =
    {
        Make("bot-01", "Robin K.", GameLogic.SoloDifficulty.Easy,    5f, 16f),
        Make("bot-02", "Sam V.",   GameLogic.SoloDifficulty.Easy,   60f, 180f),

        Make("bot-03", "Alex P.",  GameLogic.SoloDifficulty.Medium,  5f, 16f),
        Make("bot-04", "Noor A.",  GameLogic.SoloDifficulty.Medium, 25f, 70f),
        Make("bot-05", "Jules M.", GameLogic.SoloDifficulty.Medium, 60f, 180f),

        Make("bot-06", "Kit B.",   GameLogic.SoloDifficulty.Hard,    5f, 16f),
        Make("bot-07", "Remy D.",  GameLogic.SoloDifficulty.Hard,   25f, 70f),
        Make("bot-08", "Toni L.",  GameLogic.SoloDifficulty.Hard,   60f, 180f),

        Make("bot-09", "Ash W.",   GameLogic.SoloDifficulty.Expert,  5f, 16f),
        Make("bot-10", "Mika S.",  GameLogic.SoloDifficulty.Expert, 60f, 180f)
    };

    private static Player Make(string uid, string name, GameLogic.SoloDifficulty level,
                               float quickest, float slowest)
    {
        return new Player
        {
            uid = uid,
            name = name,
            level = level,
            quickestMinutes = quickest,
            slowestMinutes = slowest
        };
    }

    // Long enough that two people looking at the same moment still find each
    // other, short enough that quick game does not read as broken.
    public const float SecondsBeforeTakingTheSeat = 25f;

    public static bool Is(string uid)
    {
        return !string.IsNullOrEmpty(uid) && uid.StartsWith(UidPrefix);
    }

    public static Player PickSomebody()
    {
        return Everyone[Random.Range(0, Everyone.Length)];
    }

    public static bool Find(string uid, out Player found)
    {
        foreach (Player player in Everyone)
        {
            if (player.uid == uid)
            {
                found = player;
                return true;
            }
        }

        found = Everyone[0];

        return false;
    }

    public static GameLogic.SoloDifficulty LevelOf(string uid)
    {
        Player found;

        return Find(uid, out found) ? found.level : GameLogic.SoloDifficulty.Medium;
    }

    // How long this one takes to answer, in milliseconds.
    public static long ThinkingTimeMs(string uid)
    {
        Player found;

        if (!Find(uid, out found))
            return (long)(10f * 60f * 1000f);

        float minutes = Random.Range(found.quickestMinutes, found.slowestMinutes);

        return (long)(minutes * 60f * 1000f);
    }

    public static string NameOf(string uid)
    {
        Player found;

        return Find(uid, out found) ? found.name : "";
    }

    // What to print for an opponent. Every name on screen comes through here,
    // so whatever is decided about telling the player is decided once, in one
    // place, rather than remembered in eight.
    public static string Label(string uid, string name)
    {
        if (!Is(uid))
            return name;

        // Indistinguishable from a person by design. To mark it again, this
        // is the line: name + " (computer)".
        if (!string.IsNullOrWhiteSpace(name))
            return name.Trim();

        string known = NameOf(uid);

        return string.IsNullOrEmpty(known) ? "Scrabby" : known;
    }
}
