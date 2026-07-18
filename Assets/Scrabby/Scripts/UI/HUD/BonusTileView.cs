using UnityEngine;
using UnityEngine.UI;

public class BonusTileView : MonoBehaviour
{
    [SerializeField] private Text bonusText;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Color blankColor = Color.white;
    [SerializeField] private Color doubleLetterColor = Color.cyan;
    [SerializeField] private Color tripleLetterColor = Color.blue;
    [SerializeField] private Color doubleWordColor = Color.magenta;
    [SerializeField] private Color tripleWordColor = Color.red;

    public void Setup(BonusTile bonusTile)
    {
        if (bonusTile == null)
            return;

        switch (bonusTile.bonusType)
        {
            case BonusType.Blank:
                bonusText.text = "";
                backgroundImage.color = blankColor;
                break;

            case BonusType.DoubleLetter:
                bonusText.text = "DL";
                backgroundImage.color = doubleLetterColor;
                break;

            case BonusType.TripleLetter:
                bonusText.text = "TL";
                backgroundImage.color = tripleLetterColor;
                break;

            case BonusType.DoubleWord:
                bonusText.text = "DW";
                backgroundImage.color = doubleWordColor;
                break;

            case BonusType.TripleWord:
                bonusText.text = "TW";
                backgroundImage.color = tripleWordColor;
                break;
        }
    }
}