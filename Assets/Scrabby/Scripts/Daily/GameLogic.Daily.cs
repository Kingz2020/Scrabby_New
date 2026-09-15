using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Generating one day's puzzle.
//
// The board is not invented: it is produced by running the game's own round
// loop a few times with a seeded generator, so every position is legal by
// construction rather than by arithmetic. The seeded draws come from Unity's
// own generator, put back where it was afterwards, so nothing else in the game
// notices that generation happened.
public partial class GameLogic
{
    // Six, the same as every other game in Scrabby. The daily is the same
    // game with one turn in it, so it deals the same hand.
    private const int DefaultHandSize = 6;

    private const int MinOpeningWords = 1;      // never zero: an empty board has
    private const int MaxOpeningWords = 4;      // nothing to play off

    // A day is worth playing if the best move is worth finding and is clearly
    // better than the lazy one.
    //
    // Set from a 20-day probe rather than guessed. Best scores ran 18 to 82,
    // median 27: the original floor of 40 sat at about the 85th percentile and
    // rejected sixteen of twenty days on its own, which is a rejected
    // distribution rather than a filter. 30 keeps the upper half.
    //
    // The spread threshold survived the same check unchanged - the run
    // averaged 1.37x, and the days it rejects are the ones whose best move
    // touches no bonus square at all, which is exactly the dull day it is
    // meant to catch.
    public const int MinBestScore = 30;
    public const float MinSpread = 1.4f;

    // A day has to offer a move at every level, or the result screen grades
    // people against rungs they could not have reached. Expert is the best
    // move itself and so is always covered; the real test is that Easy,
    // Medium and Hard each have something findable.
    public const int MinBandsCovered = 4;

    // How many racks to try on one board before developing it further. The
    // rack is the cheap thing to change, so it is changed first.
    private const int RacksPerBoard = 2;

    private bool dailyGenerationCancelled;

    // Stops background generation at the next safe point. Killing the coroutine
    // outright would be worse than useless: generation works on the real board
    // and bag, and the restore only runs when it finishes, so an aborted run
    // would leave a game being played on the daily puzzle's position.
    public void CancelDailyGeneration()
    {
        dailyGenerationCancelled = true;
    }

    // Rather than fix the number of opening words up front and throw the whole
    // day away when the puzzle turns out dull, the board is grown until it is
    // interesting: draw a rack, look at what it is worth, and if there is
    // nothing to find, play another word onto the board and look again.
    //
    // Nothing is discarded, so a day effectively always produces a puzzle, and
    // openingWords stops being a dice roll - it becomes the answer to "how far
    // did this board have to develop before it got interesting".
    public IEnumerator GenerateDaily(int dayNumber, Action<DailyBoard> onComplete)
    {
        UnityEngine.Random.State entryState = UnityEngine.Random.state;

        // Generation plays on the real board arrays and draws from the real
        // bag, so anything already using them has to be handed back exactly as
        // it was. This runs quietly in the background while a player is on a
        // menu, and must stay invisible if they start a game halfway through.
        LetterInfo[,] entryBoard = validatedBoardTiles;
        BonusTile[,] entryBonuses = boardBonusTiles;
        List<LetterInfo> entryHand = playerHandTiles;
        List<LetterInfo> entryBag =
            _tileBag != null ? new List<LetterInfo>(_tileBag.GetLetters()) : null;

        dailyGenerationCancelled = false;

        DailyBoard day = new DailyBoard { dayNumber = dayNumber };

        // ---- geometry, which no game has necessarily set up -----------------
        // Board size and hand size are only ever assigned by InitGame, so a day
        // generated before any game has started would run with all three at
        // zero: a zero-length rack, and a board loop that never executes. That
        // fails silently - empty racks and no anchors - so it is established
        // here rather than assumed.
        if (!EnsureDailyGeometry())
        {
            RestoreAfterGeneration(entryBoard, entryBonuses, entryHand, entryBag, entryState);
            onComplete?.Invoke(day);
            yield break;
        }

        // ---- a clean board --------------------------------------------------
        validatedBoardTiles = new LetterInfo[boardSizeX + 2, boardSizeY + 2];
        boardBonusTiles = new BonusTile[boardSizeX, boardSizeY];

        if (playerHandTiles == null)
            playerHandTiles = new List<LetterInfo>();
        else
            playerHandTiles.Clear();

        // A full bag, every day. Generation draws and returns as it goes, so
        // without this the second day would start from the first day's
        // leftovers - which makes each day depend on the ones generated before
        // it. A day has to be reproducible on its own.
        if (!ResetDailyBag(dayNumber))
        {
            RestoreAfterGeneration(entryBoard, entryBonuses, entryHand, entryBag, entryState);
            onComplete?.Invoke(day);
            yield break;
        }

        // ---- bonus squares --------------------------------------------------
        UnityEngine.Random.InitState(
            DailySeed.SeedFor(dayNumber, DailyStream.BonusBoard));

        if (bonusTileBag != null && bonusBag != null)
        {
            bonusTileBag.ResetBonusBag(bonusBag);
        }
        else
        {
            // Without bonus squares every day scores its plain value, so the
            // spread is always 1.00x and the quality gate rejects everything
            // for a reason that has nothing to do with the day.
            Debug.LogError(
                "[DAILY] Day " + dayNumber + ": no bonus bag on GameLogic, so " +
                "the board will have no bonus squares.");
        }

        PlaceBonusTilesOnBoard();

        // ---- grow the board until the puzzle on it is worth solving ---------
        DailyBoard best = null;
        int racksTried = 0;

        for (int words = 1; words <= MaxOpeningWords && !dailyGenerationCancelled;
             words++)
        {
            // Never zero words: an empty board has nothing to play off, so one
            // is laid down before the first rack is ever drawn.
            bool placed = false;

            yield return StartCoroutine(
                PlaceOpeningWord(dayNumber, words, ok => placed = ok));

            if (!placed)
            {
                // Nothing playable from that rack. The board is as developed as
                // it is going to get, so stop growing and keep what we have.
                break;
            }

            for (int attempt = 0;
                 attempt < RacksPerBoard && !dailyGenerationCancelled;
                 attempt++)
            {
                DailyBoard candidate = null;

                yield return StartCoroutine(
                    EvaluateRack(dayNumber, words, attempt, c => candidate = c));

                if (candidate == null)
                    continue;

                racksTried++;

                // Keep the strongest thing seen, so a day that never clears the
                // gate still returns its best position rather than nothing.
                if (best == null || Better(candidate, best))
                    best = candidate;

                if (IsDayWorthPlaying(candidate))
                {
                    candidate.attempts = racksTried;
                    RestoreAfterGeneration(entryBoard, entryBonuses, entryHand, entryBag, entryState);
                    onComplete?.Invoke(candidate);
                    yield break;
                }
            }
        }

        if (dailyGenerationCancelled)
        {
            // Deliberately empty: a half-grown board is not a puzzle, and the
            // caller should try again later rather than cache this.
            day = new DailyBoard { dayNumber = dayNumber };
        }
        else if (best != null)
        {
            best.attempts = racksTried;
            day = best;
        }

        RestoreAfterGeneration(entryBoard, entryBonuses, entryHand, entryBag, entryState);

        onComplete?.Invoke(day);
    }

    private void RestoreAfterGeneration(
        LetterInfo[,] board,
        BonusTile[,] bonuses,
        List<LetterInfo> hand,
        List<LetterInfo> bag,
        UnityEngine.Random.State randomState)
    {
        validatedBoardTiles = board;
        boardBonusTiles = bonuses;
        playerHandTiles = hand;

        if (bag != null && _tileBag != null)
        {
            List<LetterInfo> letters = _tileBag.GetLetters();
            letters.Clear();
            letters.AddRange(bag);
        }

        UnityEngine.Random.state = randomState;
    }

    // Ranks two candidates when neither clears the gate. Spread first: a day
    // with something to discover beats a day that is merely high-scoring.
    private static bool Better(DailyBoard a, DailyBoard b)
    {
        // Spread is only measured on candidates that got far enough to be
        // worth measuring, so when either side lacks it there is nothing to
        // compare but the score.
        if (a.Spread > 0f && b.Spread > 0f &&
            Mathf.Abs(a.Spread - b.Spread) > 0.01f)
        {
            return a.Spread > b.Spread;
        }

        if (a.bandsCovered != b.bandsCovered)
            return a.bandsCovered > b.bandsCovered;

        return a.bestScore > b.bestScore;
    }

    // Plays one more word onto the board, using the game's own solver so the
    // position stays legal by construction. Reports false when the rack drawn
    // for it had nothing playable.
    private IEnumerator PlaceOpeningWord(
        int dayNumber, int wordIndex, Action<bool> onComplete)
    {
        UnityEngine.Random.InitState(
            DailySeed.SeedFor(dayNumber * 1000 + wordIndex, DailyStream.TileBag));

        List<LetterInfo> openingRack = DrawRack(maxHandSize);

        if (openingRack.Count == 0)
        {
            Debug.LogError(
                "[DAILY] Day " + dayNumber + ": could not draw an opening rack" +
                " (bag holds " +
                (_tileBag != null ? _tileBag.GetLetters().Count : -1) +
                " tiles). The day will be empty.");

            onComplete?.Invoke(false);
            yield break;
        }

        RoundMove move = null;

        if (!HasAnyValidatedTilesOnBoard())
        {
            // An empty board is its own case, and the game has a separate
            // search for it - the general one hangs moves off existing tiles,
            // and with nothing to hang off it does not reliably find anything.
            // The solo AI branches the same way on its first turn; generation
            // has to branch with it rather than assume one search covers both.
            move = FindBestFirstTurnPlacementGaddag(openingRack, boardBonusTiles);

            if (move != null && !move.isValid)
                move = null;
        }
        else
        {
            yield return StartCoroutine(
                SolveBestMove(openingRack, boardBonusTiles, m => move = m));
        }

        if (move == null)
        {
            Debug.LogError(
                "[DAILY] Day " + dayNumber + ": no playable opening move from [" +
                DescribeRack(openingRack) + "] with " + wordIndex +
                " word(s) already down. The day will stop developing here.");

            ReturnUnusedTiles(openingRack, null);
            onComplete?.Invoke(false);
            yield break;
        }

        PlaceDailyMove(move);
        ReturnUnusedTiles(openingRack, move);

        onComplete?.Invoke(true);
    }

    // Draws a rack on the current board and works out what it is worth: the
    // best move available, and the best a player gets who never looks for a
    // multiplier. The tiles go back afterwards either way, so the next attempt
    // draws from the same bag this one did.
    private IEnumerator EvaluateRack(
        int dayNumber, int words, int attempt, Action<DailyBoard> onComplete)
    {
        UnityEngine.Random.InitState(
            DailySeed.SeedFor(
                dayNumber * 1000 + words * 10 + attempt, DailyStream.PlayerRack));

        List<LetterInfo> rack = DrawRack(maxHandSize);

        if (rack.Count == 0)
        {
            onComplete?.Invoke(null);
            yield break;
        }

        RoundMove best = null;

        yield return StartCoroutine(
            SolveBestMove(rack, boardBonusTiles, m => best = m));

        // Read before the next search overwrites it: this is everything the
        // board offered on the real bonus layout, which is what the player is
        // actually choosing between.
        List<int> options = LastSearchScores();

        DailyBoard candidate = new DailyBoard
        {
            dayNumber = dayNumber,
            openingWords = words,
            rack = new List<LetterInfo>(rack),
            bestWord = best != null ? best.word : "",
            bestScore = best != null ? best.score : 0
        };

        DescribeSpectrum(candidate, options);
        CaptureBoard(candidate);
        candidate.bestTiles = TilesOf(best);

        // The no-bonus solve is a second full search, and it exists only to
        // work out the spread. A rack that already fails on score or on band
        // coverage is rejected whatever the spread turns out to be, so paying
        // for it would double the cost of exactly the racks we are throwing
        // away. obviousScore stays zero on those, which Better() understands.
        if (candidate.bestScore >= MinBestScore &&
            candidate.bandsCovered >= MinBandsCovered)
        {
            RoundMove plain = null;
            BonusTile[,] noBonuses = new BonusTile[boardSizeX, boardSizeY];

            yield return StartCoroutine(
                SolveBestMove(rack, noBonuses, m => plain = m));

            candidate.obviousScore = plain != null ? plain.score : 0;
        }

        ReturnUnusedTiles(rack, null);

        onComplete?.Invoke(candidate);
    }


    // Works out whether the position has something for a player of each level,
    // using the same bands the solo AI plays to. Reusing them is the point:
    // the result screen tells a player they played at Hard, so Hard has to
    // mean the same thing there as it does in a game.
    private static readonly SoloDifficulty[] Ladder =
    {
        SoloDifficulty.Easy,
        SoloDifficulty.Medium,
        SoloDifficulty.Hard,
        SoloDifficulty.Expert
    };

    private void DescribeSpectrum(DailyBoard day, List<int> options)
    {
        if (day == null || options == null || day.bestScore <= 0)
            return;

        HashSet<int> distinct = new HashSet<int>();

        foreach (int score in options)
            if (score > 0)
                distinct.Add(score);

        day.distinctScores = distinct.Count;

        int covered = 0;

        foreach (SoloDifficulty level in Ladder)
        {
            float lowFraction;
            float highFraction;

            GetDifficultyScoreBand(level, out lowFraction, out highFraction);

            // Inside the band, not merely above it. The best move sits above
            // every threshold, so testing ">= low" would count all four bands
            // as covered on any day with a single playable move - a gate that
            // always passes and measures nothing.
            float low = day.bestScore * lowFraction;
            float high = day.bestScore * highFraction;

            foreach (int score in distinct)
            {
                if (score >= low - 0.5f && score <= high + 0.5f)
                {
                    covered++;
                    break;
                }
            }
        }

        day.bandsCovered = covered;
    }

    // Worth playing, or worth skipping and regenerating.
    public static bool IsDayWorthPlaying(DailyBoard day)
    {
        return day != null &&
               day.Solved &&
               day.bestScore >= MinBestScore &&
               day.Spread >= MinSpread &&
               day.bandsCovered >= MinBandsCovered;
    }

    // The letter distribution lives in DebugManager's JSON rather than on
    // GameLogic, so generation has to ask for it there. Returns false when it
    // cannot be found, because every later step would silently produce nothing.
    // Board and hand dimensions, without starting a game. Returns false only if
    // the board cannot be measured at all, which would make everything after it
    // meaningless.
    private bool EnsureDailyGeometry()
    {
        if (boardSizeX <= 0 || boardSizeY <= 0)
        {
            // Inactive objects are included because a day may be generated
            // while the player sits on a menu with the game field switched off.
            // RowX/RowY are inspector values, readable either way.
            //
            // That widens the search to disused BoardGen objects too - the
            // scene currently carries an empty, inactive "GameField 7x1" that
            // nothing plays on - and FindAnyObjectByType would return an
            // arbitrary one of them. The largest grid is the playing board.
            BoardGen[] boardGens = UnityEngine.Object.FindObjectsByType<BoardGen>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            BoardGen board = null;
            int largest = 0;

            foreach (BoardGen candidate in boardGens)
            {
                int cells = candidate.RowX * candidate.RowY;

                if (cells > largest)
                {
                    largest = cells;
                    board = candidate;
                }
            }

            if (board == null)
            {
                Debug.LogError(
                    "[DAILY] No BoardGen in the scene, so the board size is " +
                    "unknown and no move could be placed.");
                return false;
            }

            boardSizeX = board.RowX;
            boardSizeY = board.RowY;

            Debug.Log("[DAILY] Generating on a " + boardSizeX + " x " +
                      boardSizeY + " board (" + board.name + ").");
        }

        if (boardSizeX <= 0 || boardSizeY <= 0)
        {
            Debug.LogError("[DAILY] BoardGen reported a board of " +
                           boardSizeX + " x " + boardSizeY + ".");
            return false;
        }

        // Always, not just when unset. A day has to be the same puzzle for
        // everyone on the date, and starting a solo game leaves maxHandSize at
        // whatever that game wanted - so a daily generated after one would
        // deal a different sized rack, and be a different puzzle.
        maxHandSize = DefaultHandSize;

        // The solver scores against the dictionary, which InitGame would
        // normally have loaded. Only pay for it once across a probe run.
        if (scrabbleWordSet == null || scrabbleWordSet.Count == 0)
            LoadDictionaryIfNeeded();

        EnsureAIGaddagReady();

        return true;
    }

    private bool ResetDailyBag(int dayNumber)
    {
        if (_tileBag == null)
        {
            Debug.LogError("[DAILY] Day " + dayNumber + ": no TileBag on GameLogic.");
            return false;
        }

        LetterBag bag = letterBag;

        if (!HasLetters(bag) && Singleton.Instance != null &&
            Singleton.Instance.DebugManager != null)
        {
            DebugManager debug = Singleton.Instance.DebugManager;

            // Not a null check: the scene serialises letterBag as a real object
            // with an empty letters array, so it is never null and testing for
            // that meant the JSON was never parsed. Empty and missing have to
            // mean the same thing here, or the bag silently stays empty.
            if (!HasLetters(debug.letterBag))
                debug.LoadFromJson();

            bag = debug.letterBag;
        }

        if (!HasLetters(bag))
        {
            Debug.LogError(
                "[DAILY] Day " + dayNumber + ": no letter distribution available, " +
                "so the bag would be empty and nothing could be solved.");
            return false;
        }

        _tileBag.ResetLetterBag(bag);
        return true;
    }

    // Puts back the tiles an opening word did not place. Matching is by letter
    // rather than by reference: the move carries copies, not the rack objects.
    private void ReturnUnusedTiles(List<LetterInfo> rack, RoundMove move)
    {
        if (rack == null || _tileBag == null)
            return;

        List<string> used = new List<string>();

        if (move != null && move.simulatedTiles != null)
        {
            foreach (SimPlacedTile sim in move.simulatedTiles)
            {
                if (sim != null && sim.letterInfo != null)
                    used.Add(sim.letterInfo.letter);
            }
        }

        List<LetterInfo> bag = _tileBag.GetLetters();

        foreach (LetterInfo tile in rack)
        {
            if (tile == null)
                continue;

            int at = used.IndexOf(tile.letter);

            if (at >= 0)
                used.RemoveAt(at);     // this one was played
            else
                bag.Add(tile);
        }
    }

    // Snapshots the opening position as it stands for this candidate. Taken
    // per candidate rather than at the end, because the board keeps growing
    // between attempts and the day that wins is the board as it was when it
    // won.
    // The tiles a move puts down, flattened so they survive being stored.
    private static List<DailyTile> TilesOf(RoundMove move)
    {
        List<DailyTile> tiles = new List<DailyTile>();

        if (move == null || move.simulatedTiles == null)
            return tiles;

        foreach (SimPlacedTile sim in move.simulatedTiles)
        {
            if (sim == null || sim.letterInfo == null || sim.letterPosition == null)
                continue;

            tiles.Add(new DailyTile
            {
                letter = sim.letterInfo.letter,
                points = sim.letterInfo.points,
                row = sim.letterPosition.RowX,
                col = sim.letterPosition.ColY
            });
        }

        return tiles;
    }

    private void CaptureBoard(DailyBoard day)
    {
        if (day == null || validatedBoardTiles == null)
            return;

        day.placedTiles = new List<DailyTile>();

        for (int row = 1; row <= boardSizeX; row++)
        {
            for (int col = 1; col <= boardSizeY; col++)
            {
                LetterInfo tile = validatedBoardTiles[row, col];

                if (tile == null)
                    continue;

                day.placedTiles.Add(new DailyTile
                {
                    letter = tile.letter,
                    points = tile.points,
                    row = row,
                    col = col
                });
            }
        }
    }

    private static bool HasLetters(LetterBag bag)
    {
        return bag != null && bag.letters != null && bag.letters.Length > 0;
    }

    private static string DescribeRack(List<LetterInfo> rack)
    {
        if (rack == null || rack.Count == 0)
            return "";

        string[] letters = new string[rack.Count];

        for (int i = 0; i < rack.Count; i++)
            letters[i] = rack[i] != null ? rack[i].letter : "?";

        return string.Join(" ", letters);
    }

    private List<LetterInfo> DrawRack(int count)
    {
        List<LetterInfo> rack = new List<LetterInfo>();

        if (_tileBag == null)
            return rack;

        for (int i = 0; i < count; i++)
        {
            LetterInfo tile = _tileBag.DrawLetterTileFromBag();

            if (tile == null)
                break;

            rack.Add(tile);
        }

        return rack;
    }

    // Writes a solved move onto the board model. No visuals: generation runs
    // ahead of anything being shown, and may run for a day nobody is looking at.
    private void PlaceDailyMove(RoundMove move)
    {
        if (move == null || move.simulatedTiles == null)
            return;

        foreach (SimPlacedTile sim in move.simulatedTiles)
        {
            if (sim == null || sim.letterInfo == null || sim.letterPosition == null)
                continue;

            int row = sim.letterPosition.RowX;
            int col = sim.letterPosition.ColY;

            if (row < 1 || row > boardSizeX || col < 1 || col > boardSizeY)
                continue;

            LetterInfo placed = new LetterInfo(sim.letterInfo);
            placed.bonusUsed = true;

            validatedBoardTiles[row, col] = placed;
        }
    }
}
