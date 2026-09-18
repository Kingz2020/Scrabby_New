using UnityEngine;

// One switch for the running commentary.
//
// The online and lobby code narrated every step it took - profile reads, room
// watches, list rebuilds - and filled the console to its 999 limit within a
// couple of minutes, which buries the one line that matters when something
// actually goes wrong.
//
// Those lines are now ScrabbyLog.Trace and off by default, here and in a
// build. They can be turned back on while chasing something, from
// Scrabby > Logs in the editor menu, and the setting sticks.
//
// Warnings and errors are never routed through this: something that went
// wrong should say so without anyone having asked.
public static class ScrabbyLog
{
    private const string Key = "Scrabby.VerboseLogs";

    // TESTING ONLY - set back to false before a release build.
    //
    // A phone has no menu to turn logging on, so a test build that goes quiet
    // when something odd happens tells us nothing. With this true, a build
    // talks unless someone has explicitly turned logging off.
    private const bool VerboseByDefault = true;

    private static bool loaded;
    private static bool verbose;

    public static bool Verbose
    {
        get
        {
            if (!loaded)
            {
                verbose = PlayerPrefs.GetInt(Key, VerboseByDefault ? 1 : 0) != 0;
                loaded = true;
            }

            return verbose;
        }

        set
        {
            verbose = value;
            loaded = true;
            PlayerPrefs.SetInt(Key, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    // A step worth reading only when following the steps.
    public static void Trace(object message)
    {
        if (Verbose)
            Debug.Log(message);
    }
}
