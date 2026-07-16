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
    public GameObject gameOverPanel;
    public TextMeshProUGUI gameOverSummaryText;

    [SerializeField] private TextMeshProUGUI roundMessageText;
    [SerializeField] private TextMeshProUGUI humanScoreText;
    [SerializeField] private TextMeshProUGUI aiScoreText;
    [SerializeField] private TextMeshProUGUI roundText;

    [Header("Validated Score Popup")]
    [SerializeField] private RectTransform overlayCanvasRect;
    [SerializeField] private GameObject validatedScorePopupPrefab;
    [SerializeField] private Vector2 validatedScorePopupOffset = new Vector2(40f, -40f);
    //[SerializeField] private float validatedScorePopupLifetime = 1.5f;

    public void SetTextReferences(TextMeshProUGUI human, TextMeshProUGUI ai, TextMeshProUGUI round)
    {
        humanScoreText = human;
        aiScoreText = ai;
        roundText = round;
    }

    public void AddTileToHand(LetterInfo tileInfo)
    {
        //Debug.Log("UIManager.AddTileToHand called for " + tileInfo.letter);
        GameObject tempTile = Instantiate(basicTile);
        tempTile.transform.SetParent(handTileHolder.transform, false);
        tempTile.GetComponent<TileScript>().InitTile(tileInfo);
    }

    public void ReturnTilesToHand()
    {
        List<PlacedTile> droppedTiles = Singleton.Instance.DropManager.GetTilesDroppedThisTurn();

        foreach (PlacedTile tile in droppedTiles)
        {
            if (tile == null || tile.letterInfo == null || tile.letterPosition == null)
                continue;

            TileScript[] allTileScripts = gameBoard.GetComponentsInChildren<TileScript>(true);

            foreach (TileScript tileScript in allTileScripts)
            {
                if (tileScript == null || tileScript.PlacedTileData == null)
                    continue;

                PlacedTile visualPlacedTile = tileScript.PlacedTileData;

                if (visualPlacedTile.letterInfo == null || visualPlacedTile.letterPosition == null)
                    continue;

                if (visualPlacedTile.letterInfo.letter == tile.letterInfo.letter &&
                    visualPlacedTile.letterInfo.points == tile.letterInfo.points &&
                    visualPlacedTile.letterPosition.RowX == tile.letterPosition.RowX &&
                    visualPlacedTile.letterPosition.ColY == tile.letterPosition.ColY)
                {
                    tileScript.transform.SetParent(handTileHolder.transform, false);
                    tileScript.transform.localPosition = Vector3.zero;
                    break;
                }
            }
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
            TileScript tileScript = child.GetComponent<TileScript>();

            if (tileScript == null || tileScript.LetterInfo == null)
                continue;

            if (tileScript.LetterInfo.letter == letter && tileScript.LetterInfo.points == points)
            {
                Destroy(child.gameObject);
                return;
            }
        }

        Debug.LogWarning("RemoveSingleHandTile could not find tile " + letter + " (" + points + ") in hand UI.");
    }
    public void ClearCommittedBoardTiles()
    {
        if (gameBoard == null)
        {
            Debug.LogWarning("ClearCommittedBoardTiles: gameBoard is null.");
            return;
        }

        GhostTile[] allGhostTiles = gameBoard.GetComponentsInChildren<GhostTile>(true);

        foreach (GhostTile ghostTile in allGhostTiles)
        {
            TileScript[] placedTiles = ghostTile.GetComponentsInChildren<TileScript>(true);

            foreach (TileScript tile in placedTiles)
            {
                if (tile != null && tile.gameObject != ghostTile.gameObject)
                    Destroy(tile.gameObject);
            }
        }
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
                    if (tileScript.PlacedTileData != null)
                        tileScript.PlacedTileData.letterPosition = letterPosition;
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

    public void ShowValidatedWordScore(LetterPosition letterPosition, int score, bool isWinningMove = true)
    {
        Debug.Log("ShowValidatedWordScore CALLED");

        if (letterPosition == null)
        {
            Debug.LogWarning("ShowValidatedWordScore received null letterPosition.");
            return;
        }

        Debug.Log("letterPosition row=" + letterPosition.RowX + " col=" + letterPosition.ColY + " score=" + score + " isWinningMove=" + isWinningMove);

        // Fallback for unassigned prefab
        if (validatedScorePopupPrefab == null)
        {
#if UNITY_EDITOR
            validatedScorePopupPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ValidatedScorePopup.prefab");
#endif
        }

        if (validatedScorePopupPrefab == null)
        {
            Debug.LogWarning("ShowValidatedWordScore missing validatedScorePopupPrefab reference.");
            return;
        }

        // Fallback for unassigned canvas rect
        if (overlayCanvasRect == null)
        {
            Canvas canvas = FindAnyObjectByType<Canvas>();
            if (canvas != null)
            {
                overlayCanvasRect = canvas.GetComponent<RectTransform>();
            }
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

        RectTransform popupRect = popup.GetComponent<RectTransform>();
        Transform imgChild = popup.transform.Find("Image");

        if (popupRect == null || imgChild == null)
        {
            Debug.LogWarning("Popup does not have the expected RectTransform or 'Image' child.");
            Destroy(popup);
            return;
        }

        popupRect.localScale = Vector3.one;

        // Position the root popup exactly on the bottom-right corner of the tile in world space
        Vector3[] corners = new Vector3[4];
        RectTransform ghostRect = ghostTile.GetComponent<RectTransform>();
        if (ghostRect != null)
        {
            ghostRect.GetWorldCorners(corners);
            popupRect.position = corners[3]; // corners[3] is bottom-right corner in world space!
        }
        else
        {
            popupRect.position = ghostTile.transform.position;
        }

        // Sizing the popup container to be very large (400x250) so the massive 104f font has plenty of space and won't clip
        popupRect.sizeDelta = new Vector2(400f, 250f);

        float tileWidth = ghostRect != null ? ghostRect.sizeDelta.x : 100f;
        float tileHeight = ghostRect != null ? ghostRect.sizeDelta.y : 100f;

        // Make the background a beautifully sized circular/square score badge (60% of tile size)
        float badgeSize = Mathf.Min(tileWidth, tileHeight) * 0.60f;
        
        RectTransform imgRt = imgChild.GetComponent<RectTransform>();
        if (imgRt != null)
        {
            imgRt.anchorMin = new Vector2(0.5f, 0.5f);
            imgRt.anchorMax = new Vector2(0.5f, 0.5f);
            imgRt.pivot = new Vector2(0.5f, 0.5f);
            imgRt.anchoredPosition = Vector2.zero; // Perfectly centered inside the popup container
            imgRt.sizeDelta = new Vector2(badgeSize, badgeSize);
            imgRt.localScale = Vector3.one;
        }

        // Configure the background image on imgChild
        UnityEngine.UI.Image imgComp = imgChild.GetComponent<UnityEngine.UI.Image>();
        if (imgComp == null)
        {
            imgComp = imgChild.gameObject.AddComponent<UnityEngine.UI.Image>();
        }

        if (imgComp != null)
        {
            if (isWinningMove)
            {
                // Solid high-contrast bright golden yellow/orange tile color for validated winning moves
                imgComp.color = new Color(0.95f, 0.75f, 0.15f, 1f);
            }
            else
            {
                // Sleek, high-contrast dark slate charcoal background for tentative moves so yellow text pops out!
                imgComp.color = new Color(0.12f, 0.12f, 0.12f, 0.95f);
            }
            imgComp.raycastTarget = false;
        }

        // Add a clean dark outline around the badge background so it pops out clearly
        UnityEngine.UI.Outline shapeOutline = imgChild.GetComponent<UnityEngine.UI.Outline>();
        if (shapeOutline == null)
        {
            shapeOutline = imgChild.gameObject.AddComponent<UnityEngine.UI.Outline>();
        }
        shapeOutline.effectColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);
        shapeOutline.effectDistance = new Vector2(1.5f, -1.5f);

        // Configure the text component (disable the parent TextMeshProUGUI and create a child TextMeshProUGUI as a sibling to the image)
        TextMeshProUGUI rootText = popup.GetComponent<TextMeshProUGUI>();
        TMP_FontAsset fontAsset = null;
        Material fontMaterial = null;
        if (rootText != null)
        {
            fontAsset = rootText.font;
            fontMaterial = rootText.fontSharedMaterial;
            rootText.enabled = false; // Disable parent text renderer so it doesn't render behind the image
        }

        // Create a new TextMeshProUGUI child under the root popup so it is guaranteed to draw ON TOP of the background (Sibling Index Order)
        GameObject textGo = new GameObject("BadgeText");
        textGo.transform.SetParent(popup.transform, false);
        textGo.transform.SetAsLastSibling(); // Render last = render on top!

        RectTransform textRt = textGo.AddComponent<RectTransform>();
        if (textRt != null)
        {
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.pivot = new Vector2(0.5f, 0.5f);
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            textRt.anchoredPosition = Vector2.zero;
        }

        TextMeshProUGUI popupText = textGo.AddComponent<TextMeshProUGUI>();
        if (popupText != null)
        {
            if (fontAsset != null)
            {
                popupText.font = fontAsset;
            }
            if (fontMaterial != null)
            {
                popupText.fontSharedMaterial = fontMaterial;
            }

            popupText.gameObject.SetActive(true);
            
            // Set the clean text score directly
            popupText.text = "+" + score;
            
            // Set font size to exactly 104f (which is twice the lettering size of the tiles, 52 * 2 = 104)
            popupText.fontSize = 104f; 
            popupText.fontStyle = FontStyles.Bold;
            
            if (isWinningMove)
            {
                // Bright, highly visible positive dark/forest green color so it feels like a winning score!
                popupText.color = new Color32(0, 180, 40, 255); 
            }
            else
            {
                // Bright yellow color for tentative, unvalidated player move
                popupText.color = new Color32(0, 0, 0, 255);
            }
            
            popupText.alignment = TextAlignmentOptions.Center;
            
            popupText.enableAutoSizing = false; // Disable auto-sizing so it stays exactly at the massive 104f font size
            popupText.textWrappingMode = TextWrappingModes.NoWrap;
            popupText.overflowMode = TextOverflowModes.Overflow;

            // Use TextMeshPro's native high-quality shader outlines to add a strong black outer border to keep it super legible!
            popupText.outlineColor = new Color32(0, 0, 0, 255); // Solid black outline
            popupText.outlineWidth = 0.25f; // Strong, highly visible border thickness

            Debug.Log("Popup text set to: " + popupText.text);
        }
        else
        {
            Debug.LogWarning("Failed to create child TextMeshProUGUI on badge.");
        }

        CanvasGroup popupCanvasGroup = popup.GetComponent<CanvasGroup>();
        if (popupCanvasGroup == null)
        {
            popupCanvasGroup = popup.AddComponent<CanvasGroup>();
        }
        popupCanvasGroup.alpha = 1f;

        // Run the fade animation on the root container
        ValidatedScorePopup popupScript = popup.GetComponent<ValidatedScorePopup>();
        if (popupScript != null)
        {
            float slowLifetime = 4.0f; // Remains on screen for 4 full seconds
            popupScript.floatOffset = new Vector2(30f, 60f); // Float gently upwards
            popupScript.fadeDuration = 2.0f; // Remain fully solid for the first 2 seconds, then slowly fade out over the last 2 seconds

            Debug.Log("Playing popup animation for lifetime: " + slowLifetime);
            popupScript.Play(slowLifetime);
        }
        else
        {
            Debug.LogWarning("ValidatedScorePopup script not found on popup. Destroying after lifetime only.");
            Destroy(popup, 4.0f);
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

    public void ShowGameOverPanel(string finalMessage, string roundSummary)
    {
        gameOverPanel.SetActive(true);
        gameOverSummaryText.text = finalMessage + "\n\n" + roundSummary;
    }
}