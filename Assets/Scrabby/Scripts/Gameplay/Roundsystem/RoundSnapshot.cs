using System;
using System.Collections.Generic;

public class RoundSnapshot
{
    public List<LetterInfo> initialTiles;
    public BonusTile[,] initialBonusTiles;

    public RoundSnapshot(List<LetterInfo> sourceTiles, BonusTile[,] sourceBonusTiles)
    {
        initialTiles = CloneTiles(sourceTiles);
        initialBonusTiles = CloneBonusTiles(sourceBonusTiles);
    }

    private List<LetterInfo> CloneTiles(List<LetterInfo> source)
    {
        List<LetterInfo> clone = new List<LetterInfo>();

        if (source == null)
            return clone;

        foreach (LetterInfo tile in source)
        {
            clone.Add(tile == null ? null : new LetterInfo(tile));
        }

        return clone;
    }

    private BonusTile[,] CloneBonusTiles(BonusTile[,] source)
    {
        if (source == null)
            return null;

        int width = source.GetLength(0);
        int height = source.GetLength(1);

        BonusTile[,] clone = new BonusTile[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                clone[x, y] = source[x, y] == null ? null : new BonusTile(source[x, y]);
            }
        }

        return clone;
    }
}
