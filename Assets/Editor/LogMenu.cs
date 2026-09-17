#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// The switch for the running commentary, where it can be found: the console
// is full or it is not, and that is an editor-menu sort of question.
public static class LogMenu
{
    private const string Item = "Scrabby/Logs/Verbose Logging";

    [MenuItem(Item)]
    private static void Toggle()
    {
        ScrabbyLog.Verbose = !ScrabbyLog.Verbose;
        Debug.Log("[LOGS] Verbose logging is now " +
                  (ScrabbyLog.Verbose ? "ON." : "OFF."));
    }

    [MenuItem(Item, true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(Item, ScrabbyLog.Verbose);
        return true;
    }
}
#endif
