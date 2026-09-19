#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// A chart of real games takes real games to fill, which is a slow way to find
// out whether it is drawn right. This shows it on demand, fills it with
// something to look at, and empties it again.
public static class StatsMenu
{
    [MenuItem("Scrabby/Stats/Show the chart now")]
    public static void ShowNow()
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Play mode needed",
                "The card is built onto the running canvas.", "OK");
            return;
        }

        StatsPanel.Show();
    }

    [MenuItem("Scrabby/Stats/Fill with made-up games")]
    public static void FillWithNonsense()
    {
        Record(GameLogic.SoloDifficulty.Easy, 9, 2, 0);
        Record(GameLogic.SoloDifficulty.Medium, 6, 4, 1);
        Record(GameLogic.SoloDifficulty.Hard, 2, 6, 0);
        Record(GameLogic.SoloDifficulty.Expert, 1, 3, 0);

        Match("test-a", "uid-zia", "Zia", 4, 3, 1);
        Match("test-b", "uid-ada", "Ada", 1, 2, 0);

        Debug.Log("[STATS] Filled with made-up games.");
    }

    [MenuItem("Scrabby/Stats/Forget every game")]
    public static void Forget()
    {
        PlayerStats.Forget();
        Debug.Log("[STATS] Emptied.");
    }

    private static void Record(GameLogic.SoloDifficulty level, int won, int lost, int tied)
    {
        for (int i = 0; i < won; i++)
            PlayerStats.RecordSolo(level, PlayerStats.Result.Won);

        for (int i = 0; i < lost; i++)
            PlayerStats.RecordSolo(level, PlayerStats.Result.Lost);

        for (int i = 0; i < tied; i++)
            PlayerStats.RecordSolo(level, PlayerStats.Result.Tied);
    }

    private static void Match(
        string idPrefix, string uid, string name, int won, int lost, int tied)
    {
        int game = 0;

        for (int i = 0; i < won; i++)
            PlayerStats.RecordOnline(idPrefix + game++, uid, name, PlayerStats.Result.Won);

        for (int i = 0; i < lost; i++)
            PlayerStats.RecordOnline(idPrefix + game++, uid, name, PlayerStats.Result.Lost);

        for (int i = 0; i < tied; i++)
            PlayerStats.RecordOnline(idPrefix + game++, uid, name, PlayerStats.Result.Tied);
    }
}
#endif
