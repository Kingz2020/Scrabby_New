using System;

// Everyone must get the same board on the same day, or a daily score means
// nothing. Deriving it from the date rather than serving it keeps that true
// with no server, works offline, and lets any past day be rebuilt on demand.
//
// Nothing here touches Unity, so it can be reasoned about and tested on its own.
public static class DailySeed
{
    // Day 1. Moving this renumbers every puzzle, so it does not move.
    private static readonly DateTime Epoch = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // The puzzle number a player sees: "Daily #128".
    public static int DayNumber(DateTime utcDate)
    {
        return (int)(utcDate.Date - Epoch.Date).TotalDays + 1;
    }

    public static DateTime DateFor(int dayNumber)
    {
        return Epoch.Date.AddDays(dayNumber - 1);
    }

    public static int Today()
    {
        return DayNumber(DateTime.UtcNow);
    }

    // A day number alone is a poor seed: consecutive days give consecutive
    // seeds, and System.Random starts similar sequences from similar seeds, so
    // one day's board would resemble the next. Hashing spreads them out.
    //
    // Each part of generation takes its own stream, so adding, removing or
    // reordering a step cannot disturb the others: changing how the rack is
    // drawn must not silently change every past board's bonus squares.
    public static int SeedFor(int dayNumber, DailyStream stream)
    {
        unchecked
        {
            uint h = 2166136261u;                    // FNV-1a

            foreach (byte b in BitConverter.GetBytes(dayNumber))
            {
                h ^= b;
                h *= 16777619u;
            }

            foreach (byte b in BitConverter.GetBytes((int)stream))
            {
                h ^= b;
                h *= 16777619u;
            }

            // System.Random(int.MinValue) throws, and a negative seed is fine
            // otherwise, so only that one value needs keeping away from.
            int seed = (int)h;
            return seed == int.MinValue ? 0 : seed;
        }
    }

    public static Random RandomFor(int dayNumber, DailyStream stream)
    {
        return new Random(SeedFor(dayNumber, stream));
    }
}

// The independent streams generation draws from.
public enum DailyStream
{
    BonusBoard = 1,
    TileBag = 2,
    OpeningWords = 3,
    PlayerRack = 4
}
