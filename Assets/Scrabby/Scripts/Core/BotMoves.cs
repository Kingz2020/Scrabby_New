using System;
using System.Collections.Generic;
using UnityEngine;

// Moves a bot has decided on but is not going to play yet.
//
// An opponent who answers in three hours cannot be a coroutine waiting three
// hours: the app will be closed long before. So the move is worked out while
// the board is still on screen - that is the only moment the search has a
// board and a rack to think with - and parked here, exactly as it will be
// written, with the time it is due.
//
// Writing it later needs nothing but the database, so it can happen from any
// screen, the moment the app is next open and the time has come. From the
// player's side that is indistinguishable from an opponent who played while
// they were away, because in every respect that matters, it is.
//
// Kept on the phone, so clearing the app's data strands any match whose bot
// has not answered yet. A bot that never moves is a game that never finishes,
// which is worth knowing, but the alternative is a server that can play
// Scrabby, and that is a different project.
public static class BotMoves
{
    private const string Key = "Scrabby.Bot.Pending";

    [Serializable]
    public class Pending
    {
        public string matchId;
        public int roundNumber;
        public string botUid;
        public long dueAtUnix;

        // The submission, built and serialised at the moment it was decided.
        public string submissionJson;
    }

    [Serializable]
    private class Book
    {
        public List<Pending> moves = new List<Pending>();
    }

    private static Book cached;

    public static void Remember(Pending move)
    {
        if (move == null || string.IsNullOrEmpty(move.matchId))
            return;

        Book book = Load();

        // One move per round, however many times the question was asked.
        book.moves.RemoveAll(m => m != null &&
                                  m.matchId == move.matchId &&
                                  m.roundNumber == move.roundNumber);

        book.moves.Add(move);

        Save(book);

        double minutes = Math.Max(0,
            (move.dueAtUnix - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 60000.0);

        Debug.Log("[BOT] " + move.botUid + " will answer round " + move.roundNumber +
                  " of " + move.matchId + " in " + minutes.ToString("0.0") + " minutes.");
    }

    public static bool HasOneFor(string matchId, int roundNumber)
    {
        foreach (Pending move in Load().moves)
        {
            if (move != null && move.matchId == matchId && move.roundNumber == roundNumber)
                return true;
        }

        return false;
    }

    // Everything whose time has come.
    public static List<Pending> Due()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        List<Pending> due = new List<Pending>();

        foreach (Pending move in Load().moves)
        {
            if (move != null && move.dueAtUnix <= now)
                due.Add(move);
        }

        return due;
    }

    public static void Forget(string matchId, int roundNumber)
    {
        Book book = Load();

        int before = book.moves.Count;

        book.moves.RemoveAll(m => m != null &&
                                  m.matchId == matchId &&
                                  m.roundNumber == roundNumber);

        if (book.moves.Count != before)
            Save(book);
    }

    // Testing only: nobody waits three hours to find out whether a write
    // works.
    public static int MakeEverythingDue()
    {
        Book book = Load();
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        foreach (Pending move in book.moves)
        {
            if (move != null)
                move.dueAtUnix = now;
        }

        Save(book);

        return book.moves.Count;
    }

    public static string Describe()
    {
        Book book = Load();

        if (book.moves.Count == 0)
            return "No bot owes a move.";

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string lines = "";

        foreach (Pending move in book.moves)
        {
            if (move == null)
                continue;

            double minutes = (move.dueAtUnix - now) / 60000.0;

            lines += "\n  " + move.botUid + " owes round " + move.roundNumber +
                     " of " + move.matchId + " in " + minutes.ToString("0.0") + " min";
        }

        return "Bot moves waiting:" + lines;
    }

    // Testing only.
    public static void ForgetEverything()
    {
        cached = new Book();
        PlayerPrefs.DeleteKey(Key);
        PlayerPrefs.Save();
    }

    public static int Waiting
    {
        get { return Load().moves.Count; }
    }

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
                Debug.LogWarning("[BOT] Pending moves could not be read: " + error.Message);
            }
        }

        if (cached == null)
            cached = new Book();

        if (cached.moves == null)
            cached.moves = new List<Pending>();

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
            Debug.LogWarning("[BOT] Pending moves could not be saved: " + error.Message);
        }
    }
}
