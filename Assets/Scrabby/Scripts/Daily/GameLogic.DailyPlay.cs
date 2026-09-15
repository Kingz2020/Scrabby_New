using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Playing a daily puzzle, as opposed to generating one.
//
// A daily is not a short game: it is one position and one answer. There are no
// rounds, no opponent and no second go, so it does not run through the round
// loop at all - it lays the day's position out, hands over the rack, and waits
// for a single submission.
public partial class GameLogic
{
    private bool dailyMode;
    private DailyBoard dailyDay;
    private bool dailyAnswered;

    // What the player actually managed, once they have answered.
    private int dailyPlayerScore;
    private string dailyPlayerWord = "";

    public bool IsDailyMode { get { return dailyMode; } }
    public DailyBoard CurrentDailyDay { get { return dailyDay; } }
    public bool DailyAnswered { get { return dailyAnswered; } }
    public int DailyPlayerScore { get { return dailyPlayerScore; } }
    public string DailyPlayerWord { get { return dailyPlayerWord; } }

    // The opening words are shown in a neutral shade: nobody played them, and
    // colouring them as a move would say somebody had.
    private static readonly Color DailyOpeningColour =
        new Color(0.62f, 0.66f, 0.72f, 1f);

    public void StartDaily(DailyBoard day)
    {
        if (day == null || !day.Solved)
        {
            Debug.LogError("[DAILY] Asked to play a day that has no puzzle in it.");
            return;
        }

        // Generation may still be running in the background on this same
        // object, and StopAllCoroutines would kill it without its restore ever
        // running. Setting up the day overwrites the board anyway, so the
        // board itself is safe - but the manager has to be told, or it waits
        // forever for a run that no longer exists.
        if (DailyManager.Instance != null)
            DailyManager.Instance.Abort();

        StopAllCoroutines();
        StartCoroutine(SetUpDaily(day));
    }

    // Leaves daily mode, so an ordinary game started afterwards behaves like
    // one. Called by whatever starts solo or multiplayer.
    public void LeaveDailyMode()
    {
        dailyMode = false;
        dailyDay = null;
        dailyAnswered = false;
        dailyPlayerScore = 0;
        dailyPlayerWord = "";
    }

    private IEnumerator SetUpDaily(DailyBoard day)
    {
        dailyMode = true;
        dailyDay = day;
        dailyAnswered = false;
        dailyPlayerScore = 0;
        dailyPlayerWord = "";

        if (!EnsureDailyGeometry())
            yield break;

        // The board's cells are built by BoardGen.Start(), which does not run
        // until the gameplay panel is switched on - and the option panel
        // switches it on and starts the daily in the same frame. Laying tiles
        // out before then draws them onto a grid that does not exist yet:
        // every bonus square reports "No GhostTile found" and the opening
        // words land nowhere.
        yield return StartCoroutine(WaitForBoardCells());

        ClearBoardForNewGame();

        validatedBoardTiles = new LetterInfo[boardSizeX + 2, boardSizeY + 2];
        boardBonusTiles = new BonusTile[boardSizeX, boardSizeY];

        // Bonus squares are rebuilt from the day's seed rather than stored with
        // it, so this has to lay them out exactly the way generation did.
        UnityEngine.Random.State entryState = UnityEngine.Random.state;
        UnityEngine.Random.InitState(
            DailySeed.SeedFor(day.dayNumber, DailyStream.BonusBoard));

        if (bonusTileBag != null && bonusBag != null)
            bonusTileBag.ResetBonusBag(bonusBag);

        PlaceBonusTilesOnBoard();
        UnityEngine.Random.state = entryState;

        if (bonusBoardView != null)
            bonusBoardView.StartRevealBonusTiles(0.3f);

        UIManager ui = Singleton.Instance != null
            ? Singleton.Instance.UIManager
            : null;

        // ---- the position -------------------------------------------------
        List<SimPlacedTileData> opening = new List<SimPlacedTileData>();

        foreach (DailyTile tile in day.placedTiles)
        {
            if (tile == null)
                continue;

            validatedBoardTiles[tile.row, tile.col] =
                new LetterInfo(tile.letter, tile.points) { bonusUsed = true };

            opening.Add(new SimPlacedTileData
            {
                letter = tile.letter,
                points = tile.points,
                row = tile.row,
                col = tile.col
            });
        }

        if (ui != null && opening.Count > 0)
        {
            // The same cascade a replayed word arrives on, kept on the board
            // afterwards, and with no score pinned to it - these words were not
            // played by anyone.
            yield return StartCoroutine(
                ui.PlayMovePreview(opening, DailyOpeningColour, 0, true, false, false));
        }

        // ---- the rack -------------------------------------------------------
        if (ui != null)
            ui.RemoveAllHandTiles();

        playerHandTiles = new List<LetterInfo>();

        foreach (LetterInfo tile in day.rack)
        {
            if (tile == null)
                continue;

            LetterInfo copy = new LetterInfo(tile);
            playerHandTiles.Add(copy);

            if (ui != null)
                ui.AddTileToHand(copy);
        }

        currentState = TurnState.PlayerTurn;

        if (ui != null)
        {
            ui.ClearRoundMessage();
            ui.ShowTurnState("One word. Best you can.", UIManager.TurnTone.Yours);
        }

        int bonusCount = 0;

        for (int x = 0; x < boardSizeX; x++)
            for (int y = 0; y < boardSizeY; y++)
                if (boardBonusTiles[x, y] != null)
                    bonusCount++;

        Debug.Log("[DAILY] Day " + day.dayNumber + " set up: rack [" +
                  day.RackString() + "], best available " + day.bestScore +
                  ", " + day.placedTiles.Count + " opening tile(s) from " +
                  day.openingWords + " word(s), " + bonusCount +
                  " bonus square(s), bonusBoardView " +
                  (bonusBoardView == null ? "MISSING" : "present"));
    }

    // The single submission. Returns what it was worth; the caller shows the
    // result. Answering twice is refused rather than ignored, because a daily
    // that could be retried until it went well would not mean anything.
    public bool SubmitDailyAnswer(out int score, out string word)
    {
        score = 0;
        word = "";

        if (!dailyMode || dailyDay == null)
        {
            Debug.LogError("[DAILY] A daily answer was submitted outside a daily.");
            return false;
        }

        if (dailyAnswered)
        {
            Debug.LogWarning("[DAILY] Day " + dailyDay.dayNumber +
                             " has already been answered.");
            return false;
        }

        RoundMove move = EvaluatePlayerSubmission();

        if (move == null || !move.isValid)
            return false;

        dailyAnswered = true;
        dailyPlayerScore = move.score;
        dailyPlayerWord = move.word;

        score = move.score;
        word = move.word;

        Debug.Log("[DAILY] Day " + dailyDay.dayNumber + " answered: " +
                  word + " for " + score + " of a possible " + dailyDay.bestScore);

        return true;
    }

    // Waits for the cell grid rather than assuming a frame count. Two frames
    // covers the usual case; the loop covers a slow first build, and gives up
    // rather than hanging if the grid never appears.
    private IEnumerator WaitForBoardCells()
    {
        yield return null;
        yield return null;

        int needed = boardSizeX * boardSizeY;

        for (int frame = 0; frame < 120; frame++)
        {
            GhostTile[] cells = UnityEngine.Object.FindObjectsByType<GhostTile>(
                FindObjectsSortMode.None);

            if (cells != null && cells.Length >= needed)
                yield break;

            yield return null;
        }

        Debug.LogError(
            "[DAILY] The board's cells never appeared, so the position cannot " +
            "be laid out. Is the gameplay panel active?");
    }

    // The submit button, in daily mode. Until there is a result screen this
    // says what happened in the message line, which is enough to play a day
    // end to end and see whether the scoring is right.
    private void HandleDailySubmission()
    {
        UIManager ui = Singleton.Instance != null
            ? Singleton.Instance.UIManager
            : null;

        if (dailyAnswered)
        {
            if (ui != null)
                ui.ShowRoundMessage("You have already answered today.");

            return;
        }

        int score;
        string word;

        if (!SubmitDailyAnswer(out score, out word))
        {
            // A rejected word does not use up the one answer: the rule is one
            // submission, not one attempt at placing tiles.
            if (ui != null)
                ui.ShowRoundMessage("That is not a word you can play there.");

            return;
        }

        int best = dailyDay.bestScore;
        int percent = best > 0 ? Mathf.RoundToInt(100f * score / best) : 0;

        if (ui != null)
        {
            ui.HideTurnState();
            ui.ShowRoundMessage(
                word + " - " + score + " of " + best + " (" + percent + "%). " +
                "Best was " + dailyDay.bestWord + ".");
        }
    }

    // Which rung of the solo ladder the player's score lands on, so the result
    // can be put in the same terms a game is. Uses the game's own bands rather
    // than a second set invented for the daily.
    public static SoloDifficulty DailyLevelFor(int playerScore, int bestScore)
    {
        return BandFor(playerScore, bestScore);
    }
}
