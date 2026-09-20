#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// The walkthrough runs once, the first time the game is ever opened, which
// makes the thing most in need of watching the thing hardest to watch twice.
public static class TutorialMenu
{
    [MenuItem("Scrabby/Tutorial/Run it now")]
    public static void RunNow()
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Play mode needed",
                "The tutorial drives the real menu, so the game has to be running.",
                "OK");
            return;
        }

        TutorialFlow.Begin();
    }

    [MenuItem("Scrabby/Tutorial/Forget that it ran")]
    public static void Forget()
    {
        TutorialFlow.Forget();
        Debug.Log("[TUTORIAL] It will run again on the next launch.");
    }

    // The tutorial and the rules card are the two first-run things, and
    // testing either usually means wanting both back.
    [MenuItem("Scrabby/Tutorial/Forget everything a new player would not know")]
    public static void ForgetAll()
    {
        TutorialFlow.Forget();
        PlayerPrefs.DeleteKey("Scrabby.HowToPlay.Seen");
        PlayerPrefs.Save();

        Debug.Log("[TUTORIAL] Back to a fresh install, as far as first runs go.");
    }
}
#endif
