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
}