using System;
using System.Collections.Generic;
using UnityEngine;

// How the player has been getting on, kept across launches.
//
// A finished game used to leave nothing behind: the score was on the panel
// until it was dismissed, and then it had never happened. Beating Easy for the
// tenth time and beating Expert for the first read exactly the same.
//
// So every finished game writes one line here - which level, or which friend,
// and whether it was won, lost or tied - and StatsPanel draws them.
//
// Local, like the daily record, and for the same reason: it is the player's
// own progress, nobody else is judged by it, and clearing the app's data
// losing it is a smaller price than a server having to be asked for it.
public static class PlayerStats
{
    private const string Key = "Scrabby.Stats";

    // Enough matches remembered to stop a result being counted twice when the
    // player opens a finished match again, without the list growing forever.
    private const int MatchMemory = 200;

    public enum Result
    {
        Won,
        Lost,
        Tied
    }

    // One row of the chart. Solo rows are keyed by level and exist whether or
    // not that level has been played; a friend's row appears the first time a
    // match against them finishes.
    [Serializable]
    public class Record
    {
        public string key;
        public string name;

        public int won;
        public int lost;
        public int tied;

        public long lastPlayedUnix;

        public int Games
        {
            get { return won + lost + tied; }
        }

        public int WinPercent
        {
            get { return Games == 0 ? 0 : Mathf.RoundToInt(100f * won / Games); }
        }
    }

    [Serializable]
    private class Book
    {
        public List<Record> records = new List<Record>();

        // Match ids whose result has already been counted.
        public List<string> countedMatches = new List<string>();
    }

    private static Book cached;

    // ------------------------------------------------------------ recording --

    public static Result ResultOf(int myScore, int theirScore)
    {
        if (myScore > theirScore)
            return Result.Won;

        return myScore < theirScore ? Result.Lost : Result.Tied;
    }

    public static string SoloKey(GameLogic.SoloDifficulty level)
    {
        return "solo:" + level;
    }

    public static string OpponentKey(string opponentUid)
    {
        return "vs:" + (string.IsNullOrEmpty(opponentUid) ? "unknown" : opponentUid);
    }

    public static void RecordSolo(GameLogic.SoloDifficulty level, Result result)
    {
        Add(SoloKey(level), level.ToString(), result);

        Debug.Log("[STATS] " + level + " game " + result.ToString().ToLower() + ".");
    }

    // An online result is written when the player looks at it, and they can
    // look at a finished match as often as they like - so the match says once
    // and only once whether it was won.
    public static void RecordOnline(
        string matchId, string opponentUid, string opponentName, Result result)
    {
        if (string.IsNullOrEmpty(matchId))
            return;

        Book book = Load();

        if (book.countedMatches.Contains(matchId))
            return;

        book.countedMatches.Add(matchId);

        while (book.countedMatches.Count > MatchMemory)
            book.countedMatches.RemoveAt(0);

        string name = string.IsNullOrWhiteSpace(opponentName) ? "Opponent" : opponentName.Trim();

        Add(OpponentKey(opponentUid), name, result);

        Debug.Log("[STATS] Match against " + name + " " + result.ToString().ToLower() + ".");
    }

    private static void Add(string key, string name, Result result)
    {
        Book book = Load();
        Record record = null;

        foreach (Record candidate in book.records)
        {
            if (candidate.key == key)
            {
                record = candidate;
                break;
            }
        }

        if (record == null)
        {
            record = new Record { key = key, name = name };
            book.records.Add(record);
        }

        // People rename themselves; the newest name is the one to print.
        if (!string.IsNullOrEmpty(name))
            record.name = name;

        switch (result)
        {
            case Result.Won: record.won++; break;
            case Result.Lost: record.lost++; break;
            default: record.tied++; break;
        }

        record.lastPlayedUnix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        Save(book);
    }

    // ------------------------------------------------------------- reading --

    // All four levels, in the order they are offered, played or not: an empty
    // Expert bar next to a full Easy one is itself the story.
    public static List<Record> Solo()
    {
        List<Record> rows = new List<Record>();

        foreach (GameLogic.SoloDifficulty level in
                 (GameLogic.SoloDifficulty[])Enum.GetValues(typeof(GameLogic.SoloDifficulty)))
        {
            rows.Add(Find(SoloKey(level)) ??
                     new Record { key = SoloKey(level), name = level.ToString() });
        }

        return rows;
    }

    // Friends, most recently played first.
    public static List<Record> Opponents()
    {
        List<Record> rows = new List<Record>();

        foreach (Record record in Load().records)
        {
            if (record.key != null && record.key.StartsWith("vs:"))
                rows.Add(record);
        }

        rows.Sort((a, b) => b.lastPlayedUnix.CompareTo(a.lastPlayedUnix));

        return rows;
    }

    public static bool AnythingRecorded()
    {
        foreach (Record record in Load().records)
        {
            if (record.Games > 0)
                return true;
        }

        return false;
    }

    private static Record Find(string key)
    {
        foreach (Record record in Load().records)
        {
            if (record.key == key)
                return record;
        }

        return null;
    }

    // ------------------------------------------------------------- storage --

    private static Book Load()
    {
        if (cached != null)
            return cached;

        string json = PlayerPrefs.GetString(Key, "");

        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                cached = JsonUtility.FromJson<Book>(json);
            }
            catch (Exception error)
            {
                // A count is not worth refusing to start the game over.
                Debug.LogWarning("[STATS] Could not be read: " + error.Message);
            }
        }

        if (cached == null)
            cached = new Book();

        if (cached.records == null)
            cached.records = new List<Record>();

        if (cached.countedMatches == null)
            cached.countedMatches = new List<string>();

        return cached;
    }

    private static void Save(Book book)
    {
        cached = book;

        try
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(book));
            PlayerPrefs.Save();
        }
        catch (Exception error)
        {
            Debug.LogWarning("[STATS] Could not be saved: " + error.Message);
        }
    }

    // Testing only: back to nothing played.
    public static void Forget()
    {
        cached = null;
        PlayerPrefs.DeleteKey(Key);
        PlayerPrefs.Save();
    }
}
