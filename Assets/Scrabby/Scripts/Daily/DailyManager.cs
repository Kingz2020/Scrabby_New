using System;
using System.Collections;
using UnityEngine;

// Has today's puzzle ready before anyone asks for it.
//
// Generating a day takes seconds - the solver is run repeatedly until the
// position is worth playing - which is far too long to spend while a player
// waits on a tapped button. So it happens quietly at launch, while they are
// still on a menu, and the result is kept until the date changes.
//
// The day is cached rather than regenerated because generation is the
// expensive part, not the storage: a whole day is a handful of tiles and a few
// numbers. Nothing here talks to a server, and nothing needs an account.
public class DailyManager : MonoBehaviour
{
    // Versioned: a cached day is only worth serving if it was made the way
    // days are made now. The hand size changed from seven to six, which makes
    // every stored day the wrong puzzle - bumping this throws them away rather
    // than handing someone yesterday's rules.
    private const string CacheKey = "Scrabby.Daily.Cached.v2";

    // Generation borrows the real board and bag and hands them back when it is
    // done, so it is safe while a player is on a menu but not while one is
    // being played on. Starting a game calls Abort, which unwinds it properly.
    //
    // A small slice keeps the menu smooth; this runs while nobody is waiting on
    // it, so it can afford to be slow.
    private const double BackgroundFrameBudgetMs = 6.0;

    public static DailyManager Instance { get; private set; }

    private DailyBoard today;
    private bool generating;

    // Raised when today's puzzle becomes available, whether from the cache or
    // from a fresh generation. Subscribing after it is already ready is safe -
    // check IsReady first.
    public event Action<DailyBoard> DayReady;

    public bool IsReady
    {
        get { return today != null && today.dayNumber == DailySeed.Today(); }
    }

    public bool IsGenerating
    {
        get { return generating; }
    }

    public DailyBoard Today
    {
        get { return IsReady ? today : null; }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        Prepare();
    }

    // Loads today from the cache, or starts generating it. Safe to call more
    // than once: a day already in hand or already being worked on is left
    // alone.
    public void Prepare()
    {
        if (IsReady || generating)
            return;

        if (LoadCached())
        {
            Debug.Log("[DAILY] Day " + today.dayNumber + " read from cache.");
            RaiseReady();
            return;
        }

        StartCoroutine(GenerateInBackground());
    }

    private IEnumerator GenerateInBackground()
    {
        GameLogic logic = Singleton.Instance != null
            ? Singleton.Instance.GameLogic
            : null;

        if (logic == null)
        {
            Debug.LogError("[DAILY] No GameLogic, so today's puzzle cannot be built.");
            yield break;
        }

        generating = true;

        int dayNumber = DailySeed.Today();
        DailyBoard generated = null;

        logic.BeginOfflineSolving(BackgroundFrameBudgetMs);

        yield return logic.StartCoroutine(
            logic.GenerateDaily(dayNumber, d => generated = d));

        logic.EndOfflineSolving();

        generating = false;

        if (generated == null || !generated.Solved)
        {
            // Left uncached on purpose: a failure should be retried next launch
            // rather than remembered as today's puzzle.
            Debug.LogError("[DAILY] Day " + dayNumber + " produced no puzzle.");
            yield break;
        }

        today = generated;
        SaveCached(generated);

        Debug.Log("[DAILY] Day " + dayNumber + " ready: " + generated);

        RaiseReady();
    }

    // Called when a game is about to start on the board generation is using.
    // Today's puzzle is given up rather than half-built: the next launch, or
    // the next visit to the menu, will make it properly.
    public void Abort()
    {
        if (!generating)
            return;

        GameLogic logic = Singleton.Instance != null
            ? Singleton.Instance.GameLogic
            : null;

        if (logic != null)
            logic.CancelDailyGeneration();

        // Cleared here rather than waiting for the coroutine to notice: the
        // caller is about to stop every coroutine on GameLogic, so the run may
        // never get another frame in which to tidy up after itself. Leaving
        // this set would mean no day is ever generated again this session.
        generating = false;

        Debug.Log("[DAILY] Background generation given up: a game is starting.");
    }

    private void RaiseReady()
    {
        Action<DailyBoard> handler = DayReady;

        if (handler != null)
            handler(today);
    }

    private bool LoadCached()
    {
        string json = PlayerPrefs.GetString(CacheKey, "");

        if (string.IsNullOrEmpty(json))
            return false;

        DailyBoard cached = null;

        try
        {
            cached = JsonUtility.FromJson<DailyBoard>(json);
        }
        catch (Exception error)
        {
            // A cache written by an older version of the game is not worth
            // rescuing - it costs one generation to replace.
            Debug.LogWarning("[DAILY] Cached day could not be read: " + error.Message);
            return false;
        }

        if (cached == null || cached.dayNumber != DailySeed.Today() || !cached.Solved)
            return false;

        today = cached;
        return true;
    }

    private void SaveCached(DailyBoard day)
    {
        try
        {
            PlayerPrefs.SetString(CacheKey, JsonUtility.ToJson(day));
            PlayerPrefs.Save();
        }
        catch (Exception error)
        {
            // Not fatal: the day is in hand either way, it just will not
            // survive to the next launch.
            Debug.LogWarning("[DAILY] Today's puzzle could not be cached: " +
                             error.Message);
        }
    }
}
