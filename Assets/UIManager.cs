using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    public GameObject gameBoard;
    public GameObject handTileHolder;
    public GameObject basicTile;
    public WordlistDisplay wordlistDisplay;
    public WorldlistTitleHolder worldlistTitleHolder;

    [SerializeField] private TextMeshProUGUI roundMessageText;
    [SerializeField] private TextMeshProUGUI humanScoreText;
    [SerializeField] private TextMeshProUGUI aiScoreText;
    [SerializeField] private TextMeshProUGUI roundText;

    [Header("Validated Score Popup")]
    [SerializeField] private RectTransform overlayCanvasRect;
    [SerializeField] private GameObject validatedScorePopupPrefab;
    [SerializeField] private Vector2 validatedScorePopupOffset = new Vector2(40f, -40f);
    [SerializeField] private float validatedScorePopupLifetime = 1.5f;

    public void SetTextReferences(TextMeshProUGUI human, TextMeshProUGUI ai, TextMeshProUGUI round)
    {
        humanScoreText = human;
        aiScoreText = ai;
        roundText = round;
    }

    public void AddTileToHand(LetterInfo tileInfo)
    {
        Debug.Log("UIManager.AddTileToHand called for " + tileInfo.letter);
        GameObject tempTile = Instantiate(basicTile);
        tempTile.transform.SetParent(handTileHolder.transform, false);
        tempTile.GetComponent<TileScript>().InitTile(tileInfo);
    }

    public void ReturnTilesToHand()
    {
        foreach (PlacedTile tile in Singleton.Instance.DropManager.GetTilesDroppedThisTurn())
        {
            tile.GetComponentInChildren<TileScript>().transform.SetParent(handTileHolder.transform);
        }
        Singleton.Instance.DropManager.ResetLocations();
    }

    public void ResetDisplayWordList(List<string> letters)
    {
        worldlistTitleHolder.ResetAll();
        wordlistDisplay.ResetList(letters);
    }

    public void RemoveAllHandTiles()
    {
        for (int i = handTileHolder.transform.childCount - 1; i >= 0; i--)
        {
            Destroy(handTileHolder.transform.GetChild(i).gameObject);
        }
    }

    public void AddWord(string word)
    {
        wordlistDisplay.AddWord(word);
    }

    public void AddRedWord(string word)
    {
        wordlistDisplay.AddMissingWord(word);
    }

    public void ShowRoundMessage(string message)
    {
        if (roundMessageText != null)
            roundMessageText.text = message;
    }

    public void ClearRoundMessage()
    {
        if (roundMessageText != null)
            roundMessageText.text = "";
    }

    public void RemoveSingleHandTile(string letter, int points)
    {
        for (int i = 0; i < handTileHolder.transform.childCount; i++)
        {
            Transform child = handTileHolder.transform.GetChild(i);

            PlacedTile placedTile = child.GetComponent<PlacedTile>();
            if (placedTile == null)
                placedTile = child.GetComponentInChildren<PlacedTile>();

            if (placedTile == null || placedTile.letterInfo == null)
                continue;

            if (placedTile.letterInfo.letter == letter && placedTile.letterInfo.points == points)
            {
                Destroy(child.gameObject);
                return;
            }
        }

        Debug.LogWarning("RemoveSingleHandTile could not find tile " + letter + " (" + points + ") in hand UI.");
    }

    public void PlaceAITileOnBoard(LetterInfo tileInfo, LetterPosition letterPosition)
    {
        if (tileInfo == null || letterPosition == null)
        {
            Debug.LogWarning("PlaceAITileOnBoard received null tileInfo or letterPosition.");
            return;
        }

        GhostTile[] allGhostTiles = gameBoard.GetComponentsInChildren<GhostTile>(true);

        foreach (GhostTile ghostTile in allGhostTiles)
        {
            if (ghostTile.letterPosition != null &&
                ghostTile.letterPosition.RowX == letterPosition.RowX &&
                ghostTile.letterPosition.ColY == letterPosition.ColY)
            {
                GameObject tempTile = Instantiate(basicTile);
                tempTile.transform.SetParent(ghostTile.transform, false);

                TileScript tileScript = tempTile.GetComponent<TileScript>();
                if (tileScript != null)
                {
                    tileScript.InitTile(tileInfo);
                    tileScript.SetLockedOnBoard(true);
                }

                PlacedTile placedTile = tempTile.GetComponent<PlacedTile>();
                if (placedTile != null)
                {
                    placedTile.letterInfo = tileInfo;
                    placedTile.letterPosition = letterPosition;
                }

                return;
            }
        }

        Debug.LogWarning(
            "PlaceAITileOnBoard could not find GhostTile at row " +
            letterPosition.RowX + ", col " + letterPosition.ColY
        );
    }
    private GhostTile FindGhostTileByLetterPosition(LetterPosition letterPosition)
    {
        if (letterPosition == null || gameBoard == null)
            return null;

        GhostTile[] allGhostTiles = gameBoard.GetComponentsInChildren<GhostTile>(true);

        foreach (GhostTile ghostTile in allGhostTiles)
        {
            if (ghostTile.letterPosition != null &&
                ghostTile.letterPosition.RowX == letterPosition.RowX &&
                ghostTile.letterPosition.ColY == letterPosition.ColY)
            {
                return ghostTile;
            }
        }

        return null;
    }

    public void ShowValidatedWordScore(LetterPosition letterPosition, int score)
    {
        Debug.Log("ShowValidatedWordScore CALLED");

        if (letterPosition == null)
        {
            Debug.LogWarning("ShowValidatedWordScore received null letterPosition.");
            return;
        }

        Debug.Log("letterPosition row=" + letterPosition.RowX + " col=" + letterPosition.ColY + " score=" + score);

        if (validatedScorePopupPrefab == null)
        {
            Debug.LogWarning("ShowValidatedWordScore missing validatedScorePopupPrefab reference.");
            return;
        }

        if (overlayCanvasRect == null)
        {
            Debug.LogWarning("ShowValidatedWordScore missing overlayCanvasRect reference.");
            return;
        }

        GhostTile ghostTile = FindGhostTileByLetterPosition(letterPosition);
        if (ghostTile == null)
        {
            Debug.LogWarning(
                "ShowValidatedWordScore could not find GhostTile at row " +
                letterPosition.RowX + ", col " + letterPosition.ColY
            );
            return;
        }

        Debug.Log("GhostTile found: " + ghostTile.name);
        Debug.Log("GhostTile transform position: " + ghostTile.transform.position);

        GameObject popup = Instantiate(validatedScorePopupPrefab, overlayCanvasRect);
        Debug.Log("Popup instantiated: " + popup.name);
        Debug.Log("Popup parent: " + popup.transform.parent.name);

        RectTransform popupRect = popup.GetComponent<RectTransform>();
        if (popupRect == null)
        {
            Debug.LogWarning("Validated score popup prefab is missing RectTransform.");
            Destroy(popup);
            return;
        }

        popupRect.localScale = Vector3.one;
        popupRect.position = ghostTile.transform.position + (Vector3)validatedScorePopupOffset;

        Debug.Log("Popup position set to: " + popupRect.position);
        Debug.Log("Popup localPosition after set: " + popupRect.localPosition);
        Debug.Log("Popup anchoredPosition after set: " + popupRect.anchoredPosition);
        Debug.Log("Popup localScale after set: " + popupRect.localScale);

        TextMeshProUGUI popupText = popup.GetComponentInChildren<TextMeshProUGUI>(true);
        if (popupText != null)
        {
            popupText.gameObject.SetActive(true);
            popupText.text = "+" + score;
            popupText.fontSize = 80;
            popupText.color = Color.black;

            RectTransform textRect = popupText.GetComponent<RectTransform>();
            if (textRect != null)
            {
                textRect.anchorMin = new Vector2(0.5f, 0.5f);
                textRect.anchorMax = new Vector2(0.5f, 0.5f);
                textRect.pivot = new Vector2(0.5f, 0.5f);
                textRect.anchoredPosition = Vector2.zero;
                textRect.sizeDelta = new Vector2(200f, 100f);

                Debug.Log("Popup text rect size: " + textRect.sizeDelta);
                Debug.Log("Popup text anchoredPosition: " + textRect.anchoredPosition);
            }

            Debug.Log("Popup text set to: " + popupText.text);
            Debug.Log("Popup text color: " + popupText.color);
            Debug.Log("Popup text font size: " + popupText.fontSize);
            Debug.Log("Popup text activeSelf: " + popupText.gameObject.activeSelf);
            Debug.Log("Popup text activeInHierarchy: " + popupText.gameObject.activeInHierarchy);
        }
        else
        {
            Debug.LogWarning("Validated score popup prefab is missing TextMeshProUGUI in children.");
        }

        CanvasGroup popupCanvasGroup = popup.GetComponent<CanvasGroup>();
        if (popupCanvasGroup != null)
        {
            Debug.Log("Popup CanvasGroup alpha at spawn: " + popupCanvasGroup.alpha);
        }
        else
        {
            Debug.LogWarning("Validated score popup prefab is missing CanvasGroup.");
        }

        ValidatedScorePopup popupScript = popup.GetComponent<ValidatedScorePopup>();
        if (popupScript != null)
        {
            Debug.Log("Playing popup animation for lifetime: " + validatedScorePopupLifetime);
            popupScript.Play(validatedScorePopupLifetime);
        }
        else
        {
            Debug.LogWarning("ValidatedScorePopup script not found on popup. Destroying after lifetime only.");
            Destroy(popup, validatedScorePopupLifetime);
        }
    }

    public void UpdateTotalScores(int humanScore, int aiScore)
    {
        if (humanScoreText != null)
        {
            if (humanScoreText.gameObject.name == "DigitsText")
                humanScoreText.text = humanScore.ToString();
            else
                humanScoreText.text = "Human: " + humanScore;
        }

        if (aiScoreText != null)
        {
            if (aiScoreText.gameObject.name == "DigitsText")
                aiScoreText.text = aiScore.ToString();
            else
                aiScoreText.text = "AI: " + aiScore;
        }
    }

    public void UpdateRoundText(int currentRound, int maxRounds)
    {
        if (roundText != null)
        {
            if (roundText.gameObject.name == "DigitsText")
                roundText.text = currentRound + " / " + maxRounds;
            else
                roundText.text = "Round: " + currentRound + " / " + maxRounds;
        }
    }
}