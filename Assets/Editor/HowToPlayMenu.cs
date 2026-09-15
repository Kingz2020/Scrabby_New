#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// The rules card is shown once and then never again, which makes the thing
// worth testing the hardest thing to see twice.
public static class HowToPlayMenu
{
    [MenuItem("Scrabby/Help/Show the rules now")]
    public static void ShowNow()
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("Play mode needed",
                "The card is built onto the running canvas.", "OK");
            return;
        }

        HowToPlayPanel.Show();
    }

    [MenuItem("Scrabby/Help/Forget that the rules were seen")]
    public static void Forget()
    {
        PlayerPrefs.DeleteKey("Scrabby.HowToPlay.Seen");
        PlayerPrefs.Save();
        Debug.Log("[HELP] The rules will show again on the next launch.");
    }
}
#endif
