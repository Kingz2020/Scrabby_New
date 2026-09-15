#if UNITY_EDITOR
using System.Collections;
using UnityEditor;
using UnityEngine;

// A way into the daily puzzle before the option panel has a tab for it.
//
// This is scaffolding: once Daily is a real choice on the option panel, the
// panel starts it and this can go.
public static class DailyPlayMenu
{
    [MenuItem("Scrabby/Daily/Play today")]
    public static void PlayToday()
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog(
                "Play mode needed",
                "The board and the solver only exist while the game is running.",
                "OK");
            return;
        }

        GameLogic logic = Object.FindAnyObjectByType<GameLogic>();

        if (logic == null)
        {
            EditorUtility.DisplayDialog("No GameLogic",
                "GameLogic is not in the running scene.", "OK");
            return;
        }

        if (DailyManager.Instance == null)
        {
            EditorUtility.DisplayDialog("No DailyManager",
                "DailyManager is not in the running scene, so there is nothing " +
                "preparing today's puzzle.", "OK");
            return;
        }

        logic.StartCoroutine(PlayWhenReady(logic));
    }

    private static IEnumerator PlayWhenReady(GameLogic logic)
    {
        DailyManager manager = DailyManager.Instance;

        if (!manager.IsReady)
        {
            Debug.Log("[DAILY] Today's puzzle is not ready yet - waiting for it.");

            manager.Prepare();

            // Generation takes seconds, and asking for it early is exactly what
            // the background run exists to avoid. Waiting is the honest thing
            // to do here rather than pretending it is instant.
            while (!manager.IsReady && manager.IsGenerating)
                yield return null;
        }

        DailyBoard day = manager.Today;

        if (day == null)
        {
            Debug.LogError("[DAILY] No puzzle for today - see earlier errors.");
            yield break;
        }

        Debug.Log("[DAILY] Starting " + day);

        logic.StartDaily(day);
    }

    [MenuItem("Scrabby/Daily/Submit answer")]
    public static void SubmitAnswer()
    {
        if (!Application.isPlaying)
            return;

        GameLogic logic = Object.FindAnyObjectByType<GameLogic>();

        if (logic == null || !logic.IsDailyMode)
        {
            EditorUtility.DisplayDialog("Not in a daily",
                "Start today's puzzle first: Scrabby > Daily > Play today.", "OK");
            return;
        }

        int score;
        string word;

        if (!logic.SubmitDailyAnswer(out score, out word))
        {
            Debug.LogWarning(
                "[DAILY] That submission was not accepted - either the word was " +
                "rejected, or this day has already been answered.");
            return;
        }

        DailyBoard day = logic.CurrentDailyDay;

        float share = day.bestScore > 0 ? (float)score / day.bestScore : 0f;

        Debug.Log(
            "[DAILY RESULT] " + word + " for " + score + " of a possible " +
            day.bestScore + " (" + Mathf.RoundToInt(share * 100f) + "%) - " +
            "best was " + day.bestWord + ". You played at " +
            GameLogic.DailyLevelFor(score, day.bestScore) + ".");
    }

    // One go a day is the point, which makes testing it twice awkward. This
    // hands today back - the recorded answer and the streak with it.
    [MenuItem("Scrabby/Daily/Forget today's answer")]
    public static void ForgetAnswer()
    {
        DailyProgress.Forget();

        if (Application.isPlaying)
        {
            GameLogic logic = Object.FindAnyObjectByType<GameLogic>();

            if (logic != null)
                logic.LeaveDailyMode();
        }

        Debug.Log("[DAILY] Today's answer forgotten; it can be played again.");
    }

    [MenuItem("Scrabby/Daily/Show what has been played")]
    public static void ShowProgress()
    {
        DailyProgress.DailyRecord record = DailyProgress.Load();

        Debug.Log("[DAILY] Last played day " + record.dayNumber + ": " +
                  (string.IsNullOrEmpty(record.word) ? "nothing yet" : record.word) +
                  " for " + record.score + " | streak " + record.streak +
                  ", best " + record.bestStreak +
                  " | today is day " + DailySeed.Today());
    }

    [MenuItem("Scrabby/Daily/Clear cached day")]
    public static void ClearCache()
    {
        PlayerPrefs.DeleteKey("Scrabby.Daily.Cached");
        PlayerPrefs.DeleteKey("Scrabby.Daily.Cached.v2");
        PlayerPrefs.Save();
        Debug.Log("[DAILY] Cached day cleared; it will be generated again.");
    }
}
#endif
