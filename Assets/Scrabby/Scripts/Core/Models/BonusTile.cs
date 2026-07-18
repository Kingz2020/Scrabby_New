using System;

[Serializable]
public class BonusTile
{
    public BonusType bonusType;

    public BonusTile(BonusType bonusType)
    {
        this.bonusType = bonusType;
    }

    public BonusTile(BonusTile other)
    {
        this.bonusType = other.bonusType;
    }

    public override string ToString()
    {
        return $"{nameof(bonusType)}: {bonusType}";
    }
}