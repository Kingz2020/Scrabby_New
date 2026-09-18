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

    // A phone has no menu to turn logging on, so a test build that goes quiet
    // when something odd happens tells us nothing - but it is not free.
    // Android writes a stack trace with every line, and the AI's search alone
    // wrote up to two thousand lines a turn, which is a second or more of the
    // time the player spends watching "thinking".
    //
    // Set to true to build a chatty APK for chasing something; false is the
    // right setting the rest of the time, including release.
    private const bool VerboseByDefault = false;

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
        if (!Verbose)
            return;

        EnsureCheapLogs();
        Debug.Log(message);
    }

    // Most of the cost of a log line on a phone is the stack trace Unity
    // attaches to it, and these lines are read for what they say, not for
    // where they came from. Warnings and errors keep theirs.
    private static bool stackTracesTrimmed;

    private static void EnsureCheapLogs()
    {
        if (stackTracesTrimmed)
            return;

        stackTracesTrimmed = true;
        Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
    }
}
