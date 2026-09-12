using System.Collections.Generic;

[System.Serializable]
public class RoundResult
{
    public int roundNumber;
    public int humanScore;
    public int aiScore;
    public string humanWord;
    public string aiWord;
    public bool humanValid;
    public bool aiValid;
    public bool humanWasWinner;

    // Where each side's tiles went, so a finished solo game can be replayed.
    // Held in the same shape the replay animation already consumes. A solo game
    // never leaves the device, so unlike the online history this needs no board
    // snapshots: the board at any round is the earlier rounds' winners applied
    // in order.
    public List<SimPlacedTileData> humanTiles = new List<SimPlacedTileData>();
    public List<SimPlacedTileData> aiTiles = new List<SimPlacedTileData>();
    public List<SimPlacedTileData> winnerTiles = new List<SimPlacedTileData>();

    // Bonus squares are re-scattered every round, so a replay that does not
    // restore them shows the wrong board and the wrong reason for the score.
    public List<BonusCellSnapshot> bonusBoard = new List<BonusCellSnapshot>();
}

[System.Serializable]
public class BonusCellSnapshot
{
    public int x;
    public int y;
    public BonusType bonusType;
}