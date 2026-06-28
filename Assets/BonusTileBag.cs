using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public class BonusTileBag : MonoBehaviour
{
    private List<BonusTile> bonusTiles = new List<BonusTile>();

    public List<BonusTile> GetBonusTiles()
    {
        return bonusTiles;
    }

    public void ResetBonusBag(BonusBag bonusBag)
    {
        bonusTiles.Clear();

        foreach (BonusDistribution bonusDistribution in bonusBag.bonusTiles)
        {
            for (int amount = 0; amount < bonusDistribution.amount; amount++)
            {
                bonusTiles.Add(new BonusTile(bonusDistribution.bonusTile.bonusType));
            }
        }
    }

    public BonusTile DrawRandomBonusTile()
    {
        if (bonusTiles.Count == 0)
        {
            Debug.LogWarning("No bonus tiles left in bonus bag.");
            return null;
        }

        int index = Random.Range(0, bonusTiles.Count);
        BonusTile returnTile = bonusTiles[index];
        bonusTiles.RemoveAt(index);
        return returnTile;
    }

    public int GetRemainingCount()
    {
        return bonusTiles.Count;
    }
}