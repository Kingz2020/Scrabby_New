using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class RoundMove
{
    public bool isValid;
    public int score;
    public string word;
    public float timeUsed;
    public bool isHuman;
    public List<PlacedTile> placedTiles;
    public List<SimPlacedTile> simulatedTiles;
}