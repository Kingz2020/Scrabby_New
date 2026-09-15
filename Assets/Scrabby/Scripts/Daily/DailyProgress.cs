using System;
using UnityEngine;

// What the player has already done, kept across launches.
//
// One go a day only means something if closing the app does not hand out
// another one. The answer is written down when it is given, and read back
// before a day is dealt.
//
// This is local, and honestly so: clearing the app's data or reinstalling
// wipes it, and someone determined can replay a day. Stopping that needs an
// account and a server to be the judge of what day it is. For a puzzle where
// the only person cheated is the cheat, local is the right trade.
public static class DailyProgress
{
    private const string Key = "Scrabby.Daily.Progress";

    private static DailyRecord cached;

    // What was played, and when. Kept as one record rather than loose keys
    // because the streak belongs with it, and a record already exists.
    [Serializable]
    public class DailyRecord
    {
        public int dayNumber;          // the day this answer belongs to
        public int score;
        public string word = "";

        public int streak;             // consecutive days played
        public int bestStreak;
    }

    public static DailyRecord Load()
    {
        if (cached != null)
            return cached;

        string json = PlayerPrefs.GetString(Key, "");

        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                cached = JsonUtility.FromJson<DailyRecord>(json);
            }
            catch (Exception error)
            {
                // A record written by an older version is not worth rescuing;
                // losing a streak is better than refusing to start.
                Debug.LogWarning("[DAILY] Progress could not be read: " + error.Message);
            }
        }

        if (cached == null)
            cached = new DailyRecord();

        return cached;
    }

    // Whether today has already been answered, and with what.
    public static bool AnsweredToday(out DailyRecord record)
    {
        record = Load();

        return record.dayNumber == DailySeed.Today() &&
               !string.IsNullOrEmpty(record.word);
    }

    public static void RecordAnswer(int dayNumber, int score, string word)
    {
        DailyRecord record = Load();

        // Played yesterday as well, so the run continues; otherwise it starts
        // again at one. Playing the same day twice cannot happen - the caller
        // checks first - but it would not extend a streak if it did.
        if (record.dayNumber == dayNumber - 1 && record.streak > 0)
            record.streak++;
        else if (record.dayNumber != dayNumber)
            record.streak = 1;

        record.dayNumber = dayNumber;
        record.score = score;
        record.word = word ?? "";
        record.bestStreak = Mathf.Max(record.bestStreak, record.streak);

        Save(record);

        Debug.Log("[DAILY] Day " + dayNumber + " recorded: " + word + " for " +
                  score + ", streak " + record.streak);
    }

    private static void Save(DailyRecord record)
    {
        cached = record;

        try
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(record));
            PlayerPrefs.Save();
        }
        catch (Exception error)
        {
            // Not fatal in this session - the answer still counts until the
            // app closes - but say so, because it will not survive.
            Debug.LogWarning("[DAILY] Progress could not be saved: " + error.Message);
        }
    }

    // Testing only: hands today back.
    public static void Forget()
    {
        cached = null;
        PlayerPrefs.DeleteKey(Key);
        PlayerPrefs.Save();
    }
}
