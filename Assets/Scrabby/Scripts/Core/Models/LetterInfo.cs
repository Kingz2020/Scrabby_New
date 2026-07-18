using System;

[Serializable]
public class LetterInfo
{
    public string letter;
    public int points;
    public bool bonusUsed;

    public LetterInfo(string letter, int points)
    {
        this.letter = letter;
        this.points = points;
        this.bonusUsed = false;
    }

    public LetterInfo(LetterInfo other)
    {
        this.letter = other.letter;
        this.points = other.points;
        this.bonusUsed = other.bonusUsed;
    }

    public override string ToString()
    {
        return $"{nameof(letter)}: {letter}, {nameof(points)}: {points}, {nameof(bonusUsed)}: {bonusUsed}";
    }
}