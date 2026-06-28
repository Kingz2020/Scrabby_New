[System.Serializable]
public class LetterPosition
{
    public int RowX;
    public int ColY;

    public LetterPosition() { }

    public LetterPosition(int rowX, int colY)
    {
        RowX = rowX;
        ColY = colY;
    }
}