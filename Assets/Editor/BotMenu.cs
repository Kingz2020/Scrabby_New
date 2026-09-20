#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// A bot answers in anything from five minutes to three hours, which is right
// for the game and hopeless for testing it.
public static class BotMenu
{
    [MenuItem("Scrabby/Bots/What is owed")]
    public static void What()
    {
        Debug.Log("[BOT] " + BotMoves.Describe());
    }

    [MenuItem("Scrabby/Bots/Play owed moves now")]
    public static void PlayNow()
    {
        int count = BotMoves.MakeEverythingDue();

        if (!Application.isPlaying)
        {
            Debug.Log("[BOT] " + count + " move(s) marked due; they will be played " +
                      "within twenty seconds of entering play mode.");
            return;
        }

        if (Singleton.Instance != null && Singleton.Instance.OnlineMatchController != null)
            Singleton.Instance.OnlineMatchController.PlayDueBotMovesNow();

        Debug.Log("[BOT] Played " + count + " owed move(s).");
    }

    [MenuItem("Scrabby/Bots/Forget owed moves")]
    public static void Forget()
    {
        BotMoves.ForgetEverything();
        Debug.Log("[BOT] Pending moves cleared.");
    }
}
#endif
