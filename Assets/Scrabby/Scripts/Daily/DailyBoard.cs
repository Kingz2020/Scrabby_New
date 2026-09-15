using System;
using System.Collections.Generic;

// What one day's puzzle is, once it has been generated and solved.
//
// bestScore is the number a player is graded against, so it has to be the real
// best the solver could find on this board with this rack - not the best it
// happened to sample, and not a move picked to fit a difficulty band.
[Serializable]
public class DailyBoard
{
    public int dayNumber;

    // How far into a game the position is. Never zero: an empty board is a
    // different and worse puzzle, because there is nothing to play off.
    public int openingWords;

    public List<LetterInfo> rack = new List<LetterInfo>();

    // The opening words, as placed. Without these the day is only a set of
    // numbers - there would be nothing to put on the board, and a cached day
    // would have to be generated again to be played, which is the whole thing
    // caching is meant to avoid.
    //
    // Bonus squares are deliberately not here: they come straight from the
    // day's seed and cost nothing to lay out again, so storing them would only
    // create a second version of the truth.
    public List<DailyTile> placedTiles = new List<DailyTile>();

    public string bestWord = "";
    public int bestScore;

    // Where the best word went, not just what it was. Naming a word without
    // showing it is only half an answer: on a board this open, the place it
    // had to go is most of the puzzle.
    public List<DailyTile> bestTiles = new List<DailyTile>();

    // The score the obvious move gets - the best that uses no bonus square.
    // A day where the best and the obvious are close is a dull day, however
    // high the numbers are.
    public int obviousScore;

    // How many racks were looked at before this position was settled on. A
    // high number means the generator had to work for this day, which is worth
    // being able to see.
    public int attempts;

    // How many of the four difficulty bands have a move a player could
    // actually find there.
    //
    // This is what makes the day a puzzle rather than a lottery. One 40-point
    // play with nothing else on the board is a single guess; the result screen
    // grades people against the ladder, so each rung needs something on it or
    // a player of that level has nowhere to land.
    public int bandsCovered;

    // How many distinct scores were available at all - a blunt measure of how
    // much choice the position offers.
    public int distinctScores;

    public bool Solved
    {
        get { return bestScore > 0 && !string.IsNullOrEmpty(bestWord); }
    }

    // How much better the best move is than the obvious one, as a multiple.
    public float Spread
    {
        get { return obviousScore > 0 ? (float)bestScore / obviousScore : 0f; }
    }

    public override string ToString()
    {
        return string.Format(
            "Daily #{0}: {1} opening word(s), rack [{2}], best {3} = {4}, obvious {5}, " +
            "spread {6:0.00}x, bands {7}/4, {8} options, {9} rack(s) tried",
            dayNumber, openingWords, RackString(), bestWord, bestScore,
            obviousScore, Spread, bandsCovered, distinctScores, attempts);
    }

    public string RackString()
    {
        if (rack == null || rack.Count == 0)
            return "";

        string[] letters = new string[rack.Count];

        for (int i = 0; i < rack.Count; i++)
            letters[i] = rack[i] != null ? rack[i].letter : "?";

        return string.Join(" ", letters);
    }
}

// One tile of a day's opening position. Flat and serialisable so a whole day
// survives a round trip through JSON.
[Serializable]
public class DailyTile
{
    public string letter;
    public int points;
    public int row;
    public int col;
}
