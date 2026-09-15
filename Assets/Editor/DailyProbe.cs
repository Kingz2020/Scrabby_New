#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

// Generates a run of days and reports what came out, so the quality gate can be
// set from numbers rather than from a guess.
//
// It has to run in Play mode: the solver is a coroutine that needs the GADDAG
// loaded and a live GameLogic, neither of which exist while the editor is
// stopped.
public static class DailyProbe
{
    private const int DefaultDays = 20;

    // Generation is slow enough that a long run is worth being able to call
    // off without leaving Play mode.
    private static bool cancelled;
    private static bool running;

    [MenuItem("Scrabby/Daily/Probe 20 days")]
    public static void Probe20()
    {
        Run(DefaultDays);
    }

    [MenuItem("Scrabby/Daily/Probe 100 days")]
    public static void Probe100()
    {
        Run(100);
    }

    [MenuItem("Scrabby/Daily/Stop probe")]
    public static void Stop()
    {
        cancelled = true;
    }

    private static void Run(int days)
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog(
                "Play mode needed",
                "The solver needs the dictionary loaded and a live GameLogic, so " +
                "start Play mode and run this again.",
                "OK");
            return;
        }

        if (running)
        {
            EditorUtility.DisplayDialog("Already running",
                "A probe is already generating. Use Scrabby > Daily > Stop " +
                "probe to call it off.", "OK");
            return;
        }

        GameLogic logic = Object.FindAnyObjectByType<GameLogic>();

        if (logic == null)
        {
            EditorUtility.DisplayDialog("No GameLogic",
                "GameLogic is not in the running scene.", "OK");
            return;
        }

        // GenerateDaily refills the bag itself, but if the distribution cannot
        // be found at all then every day comes back with an empty rack and a
        // zero score, which reads like a bad generator rather than missing
        // data. Say so up front instead.
        if (Singleton.Instance == null || Singleton.Instance.DebugManager == null)
        {
            EditorUtility.DisplayDialog("No DebugManager",
                "The letter distribution is loaded from DebugManager's JSON, " +
                "which is not in the running scene.", "OK");
            return;
        }

        DebugManager debug = Singleton.Instance.DebugManager;
        debug.LoadFromJson();

        if (debug.letterBag == null || debug.letterBag.letters == null ||
            debug.letterBag.letters.Length == 0)
        {
            EditorUtility.DisplayDialog("Empty letter bag",
                "DebugManager's JSON did not parse into any letters, so every " +
                "rack would be empty.", "OK");
            return;
        }

        logic.StartCoroutine(ProbeRoutine(logic, days));
    }

    private static IEnumerator ProbeRoutine(GameLogic logic, int days)
    {
        int first = DailySeed.Today();
        List<DailyBoard> results = new List<DailyBoard>();
        StringBuilder log = new StringBuilder();

        Debug.Log("[DAILY PROBE] generating " + days + " days from #" + first +
                  "... (Scrabby > Daily > Stop probe to call it off)");

        // No framerate to protect here, and the per-search stats would run to
        // a hundred lines. Both are put back in the finally below.
        running = true;
        cancelled = false;
        // 30ms was still throwing away a rendered frame for every 30ms of
        // work - roughly a third of the time spent waiting on the editor
        // rather than searching. A budget far longer than a frame makes the
        // search effectively synchronous, which is what an offline run wants.
        // Long enough that the run is not throttled to a crawl, short enough
        // that the editor still redraws and can be stopped. At two seconds it
        // was quick but looked exactly like a hang, which is worse than slow.
        logic.BeginOfflineSolving(250.0);


        for (int i = 0; i < days && !cancelled; i++)
        {
            DailyBoard day = null;

            yield return logic.StartCoroutine(
                logic.GenerateDaily(first + i, d => day = d));

            if (day == null)
                continue;

            results.Add(day);

            string line = (GameLogic.IsDayWorthPlaying(day) ? "  ok   " : "  WEAK ") + day;
            log.AppendLine(line);

            // Reported as it happens: a long run with nothing on the console
            // is indistinguishable from a hung one.
            Debug.Log("[DAILY PROBE] " + (i + 1) + "/" + days + line);

            // one day per frame at least, so the editor stays responsive
            yield return null;
        }

        logic.EndOfflineSolving();
        running = false;

        if (cancelled)
            log.AppendLine("  (stopped early after " + results.Count + " days)");

        int good = 0, unsolved = 0, lowScore = 0, flat = 0;
        int totalBest = 0, minBest = int.MaxValue, maxBest = 0;
        float totalSpread = 0f;
        int spreadSamples = 0;
        int[] openingCounts = new int[6];

        foreach (DailyBoard d in results)
        {
            if (!d.Solved) { unsolved++; continue; }

            totalBest += d.bestScore;
            if (d.Spread > 0f)
            {
                totalSpread += d.Spread;
                spreadSamples++;
            }
            minBest = Mathf.Min(minBest, d.bestScore);
            maxBest = Mathf.Max(maxBest, d.bestScore);

            if (d.openingWords >= 0 && d.openingWords < openingCounts.Length)
                openingCounts[d.openingWords]++;

            if (GameLogic.IsDayWorthPlaying(d)) good++;
            else if (d.bestScore < GameLogic.MinBestScore) lowScore++;
            else flat++;
        }

        int solved = results.Count - unsolved;

        log.AppendLine();
        log.AppendLine("  " + results.Count + " days generated");
        log.AppendLine("  " + good + " worth playing, " + lowScore + " too low, " +
                       flat + " too flat, " + unsolved + " unsolved");

        if (solved > 0)
        {
            // The median matters more than the mean here: one seven-tile play
            // can sit forty points above everything else and drag the average
            // somewhere no actual day lives.
            List<int> scores = new List<int>();

            foreach (DailyBoard d in results)
                if (d.Solved)
                    scores.Add(d.bestScore);

            scores.Sort();
            int median = scores[scores.Count / 2];

            log.AppendLine("  best score: min " + minBest + ", median " + median +
                           ", avg " + (totalBest / solved) + ", max " + maxBest);
            // Only over days where it was actually measured: the no-bonus
            // search is skipped for racks already rejected on score, so those
            // carry a zero that means "not asked", not "no spread".
            if (spreadSamples > 0)
            {
                log.AppendLine("  average spread over the no-bonus move: " +
                               (totalSpread / spreadSamples).ToString("0.00") +
                               "x, over " + spreadSamples + " measured day(s)");
            }
        }

        if (results.Count > 0)
        {
            int totalAttempts = 0;

            foreach (DailyBoard d in results)
                totalAttempts += d.attempts;

            log.AppendLine("  racks tried per day: " +
                           ((float)totalAttempts / results.Count).ToString("0.0"));
        }

        // Now means "how far the board had to develop before it got
        // interesting" rather than a dice roll, so the shape of this is worth
        // reading: all 1s would mean the gate is too easy.
        log.Append("  opening words: ");
        for (int i = 1; i < openingCounts.Length; i++)
            if (openingCounts[i] > 0)
                log.Append(i + "x" + openingCounts[i] + "  ");

        Debug.Log("[DAILY PROBE]\n" + log);
    }
}
#endif
