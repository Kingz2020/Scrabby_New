
using System;
using System.Collections;
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

    [SerializeField] private Transform roundListContainer;
    [SerializeField] private RoundReplayRow roundReplayRowPrefab;

    private readonly List<RoundReplayRow> spawnedRoundRows =
        new List<RoundReplayRow>();

    [SerializeField] private GameObject replayPreviewTilePrefab;

    private readonly List<GameObject> replayPreviewTiles =
        new List<GameObject>();


    [SerializeField]
    private Color replayPreviewColor =
        new Color(1f, 0.82f, 0.18f, 0.75f);

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

    public TileScript CreateReplayPreviewTile(
    LetterInfo tileInfo,
    LetterPosition letterPosition)
    {
        if (tileInfo == null || letterPosition == null)
            return null;

        GhostTile[] allGhostTiles =
            gameBoard.GetComponentsInChildren<GhostTile>(true);

        foreach (GhostTile ghostTile in allGhostTiles)
        {
            if (ghostTile == null || ghostTile.letterPosition == null)
                continue;

            bool matches =
                ghostTile.letterPosition.RowX == letterPosition.RowX &&
                ghostTile.letterPosition.ColY == letterPosition.ColY;

            if (!matches)
                continue;

            GameObject tempTile = Instantiate(basicTile);
            tempTile.transform.SetParent(ghostTile.transform, false);

            TileScript tileScript =
                tempTile.GetComponent<TileScript>();

            if (tileScript == null)
            {
                Destroy(tempTile);
                return null;
            }

            tileScript.InitTile(new LetterInfo(tileInfo));
            tileScript.SetLockedOnBoard(true);

            if (tileScript.PlacedTileData != null)
            {
                tileScript.PlacedTileData.letterPosition =
                    new LetterPosition(
                        letterPosition.RowX,
                        letterPosition.ColY
                    );
            }

            return tileScript;
        }

        Debug.LogWarning(
            "CreateReplayPreviewTile could not find GhostTile at row " +
            letterPosition.RowX + ", col " + letterPosition.ColY
        );

        return null;
    }

    public IEnumerator PlayMovePreview(
    List<SimPlacedTileData> moveTiles,
    Color highlightColor,
    float totalDuration)
    {
        if (moveTiles == null || moveTiles.Count == 0)
            yield break;

        moveTiles.Sort((a, b) =>
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;

            if (a.row == b.row)
                return a.col.CompareTo(b.col);

            if (a.col == b.col)
                return a.row.CompareTo(b.row);

            int rowCompare = a.row.CompareTo(b.row);
            return rowCompare != 0
                ? rowCompare
                : a.col.CompareTo(b.col);
        });

        List<TileScript> previewTiles =
            new List<TileScript>();

        foreach (SimPlacedTileData simTile in moveTiles)
        {
            if (simTile == null)
                continue;

            TileScript previewTile = CreateReplayPreviewTile(
                new LetterInfo(simTile.letter, simTile.points),
                new LetterPosition(simTile.row, simTile.col)
            );

            if (previewTile != null)
                previewTiles.Add(previewTile);
        }

        if (previewTiles.Count == 0)
            yield break;

        float durationPerTile =
            totalDuration / previewTiles.Count;

        foreach (TileScript tile in previewTiles)
        {
            if (tile == null)
                continue;

            yield return StartCoroutine(
                tile.PlayWinningReplayDrop(
                    durationPerTile,
                    highlightColor
                )
            );
        }

        yield return new WaitForSecondsRealtime(0.5f);

        RemoveReplayPreviewTiles(previewTiles);
    }

    public void RemoveReplayPreviewTiles(
    List<TileScript> previewTiles)
    {
        if (previewTiles == null)
            return;

        foreach (TileScript tile in previewTiles)
        {
            if (tile != null)
                Destroy(tile.gameObject);
        }

        previewTiles.Clear();
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

        imgChild.gameObject.SetActive(false);

        popupRect.localScale = Vector3.one;

        // Position the root popup exactly on the bottom-right corner of the tile in world space
        Vector3[] corners = new Vector3[4];
        RectTransform ghostRect = ghostTile.GetComponent<RectTransform>();
        if (ghostRect != null)
        {
            ghostRect.GetWorldCorners(corners);
            //popupRect.position = corners[3]; // corners[3] is bottom-right corner in world space!
            popupRect.position = corners[3] + new Vector3(16f, 10f, 0f);
        }
        else
        {
            popupRect.position = ghostTile.transform.position;
        }

        popupRect.sizeDelta = new Vector2(110f, 60f);

        float tileWidth = ghostRect != null ? ghostRect.sizeDelta.x : 100f;
        float tileHeight = ghostRect != null ? ghostRect.sizeDelta.y : 100f;

        // Make the background a beautifully sized circular/square score badge (60% of tile size)
        float badgeSize = Mathf.Min(tileWidth, tileHeight) * 0.42f;
        
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

            if (isWinningMove)
            {
                popupText.fontSize = 42f;
                popupText.fontStyle = FontStyles.Bold;
                popupText.color = new Color32(0, 180, 40, 255);
                popupText.outlineColor = new Color32(0, 0, 0, 255);
                popupText.outlineWidth = 0.18f;
            }
            else
            {
                popupText.fontSize = 38f;
                popupText.fontStyle = FontStyles.Normal;
                popupText.color = new Color32(20, 20, 20, 255);
                popupText.outlineColor = new Color32(0, 0, 0, 180);
                popupText.outlineWidth = 0.08f;
            }

            popupText.alignment = TextAlignmentOptions.Center;
            popupText.enableAutoSizing = false;
            popupText.textWrappingMode = TextWrappingModes.NoWrap;
            popupText.overflowMode = TextOverflowModes.Overflow;
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

            //float popupLifetime = 0.85f;
            float popupLifetime = 5f;
            popupScript.floatOffset = new Vector2(10f, 18f);
            popupScript.fadeDuration = 0.22f;
            popupScript.Play(popupLifetime);

        }
        else
        {
            Debug.LogWarning("ValidatedScorePopup script not found on popup. Destroying after lifetime only.");
            Destroy(popup, 0.85f);
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

    public void UpdateGameOverSummary(
    string finalMessage,
    string roundSummary)
    {
        if (gameOverPanel == null || gameOverSummaryText == null)
            return;

        gameOverPanel.SetActive(true);
        gameOverSummaryText.text =
            finalMessage + "\n\n" + roundSummary;
    }

    public IEnumerator PlayWinningWordReplay(
        List<SimPlacedTileData> winningTiles,
        float totalDuration)
    {
        if (winningTiles == null || winningTiles.Count == 0)
            yield break;

        if (gameBoard == null)
        {
            Debug.LogWarning(
                "PlayWinningWordReplay: gameBoard is null."
            );
            yield break;
        }

        GhostTile[] ghosts =
            gameBoard.GetComponentsInChildren<GhostTile>(true);

        List<TileScript> tilesToAnimate = new List<TileScript>();

        winningTiles.Sort((a, b) =>
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;

            // Same row: horizontal word, animate from left to right.
            if (a.row == b.row)
                return a.col.CompareTo(b.col);

            // Same column: vertical word, animate from top to bottom.
            if (a.col == b.col)
                return a.row.CompareTo(b.row);

            // Fallback for malformed/non-linear data:
            // process upper rows first, then left-to-right within each row.
            int rowCompare = a.row.CompareTo(b.row);
            return rowCompare != 0
                ? rowCompare
                : a.col.CompareTo(b.col);
        });



        foreach (SimPlacedTileData replayTile in winningTiles)
        {
            foreach (GhostTile ghost in ghosts)
            {
                if (ghost == null || ghost.letterPosition == null)
                    continue;

                bool matchesPosition =
                    ghost.letterPosition.RowX == replayTile.row &&
                    ghost.letterPosition.ColY == replayTile.col;

                if (!matchesPosition)
                    continue;

                TileScript committedTile =
                    ghost.GetComponentInChildren<TileScript>(true);

                if (committedTile != null)
                    tilesToAnimate.Add(committedTile);

                break;
            }
        }

        if (tilesToAnimate.Count == 0)
        {
            Debug.LogWarning(
                "PlayWinningWordReplay: no committed tiles found."
            );
            yield break;
        }

        Color winningGreen =
            new Color(0.20f, 1f, 0.34f, 1f);

        float perTileDuration =
            totalDuration / tilesToAnimate.Count;

        foreach (TileScript tile in tilesToAnimate)
        {
            yield return StartCoroutine(
                tile.PlayWinningReplayDrop(
                    perTileDuration,
                    winningGreen
                )
            );
        }
    }
    public void ShowOnlineRoundReplayRows(
    List<OnlineRoundHistoryEntry> history,
    bool amPlayer1,
    string opponentName,
    Action<OnlineRoundHistoryEntry> onReplay)
    {
        ClearOnlineRoundReplayRows();

        if (roundListContainer == null ||
            roundReplayRowPrefab == null ||
            history == null)
        {
            Debug.LogWarning(
                "[UIManager] Cannot create round replay rows: missing setup."
            );
            return;
        }

        foreach (OnlineRoundHistoryEntry round in history)
        {
            if (round == null)
                continue;

            string myWord = amPlayer1
                ? round.player1Word
                : round.player2Word;

            string opponentWord = amPlayer1
                ? round.player2Word
                : round.player1Word;

            int myScore = amPlayer1
                ? round.player1Score
                : round.player2Score;

            int opponentScore = amPlayer1
                ? round.player2Score
                : round.player1Score;

            string winnerText;

            if (!round.anyValidMove)
            {
                winnerText = "No valid move";
            }
            else if (round.winnerIsPlayer1 == amPlayer1)
            {
                winnerText = "You won";
            }
            else
            {
                winnerText = opponentName + " won";
            }

            string rowText =
                $"Round {round.roundNumber}: " +
                $"{myWord} ({myScore}) vs " +
                $"{opponentWord} ({opponentScore}) — " +
                winnerText;

            RoundReplayRow row = Instantiate(
                roundReplayRowPrefab,
                roundListContainer
            );

            row.Setup(round, rowText, onReplay);

            spawnedRoundRows.Add(row);
        }
    }

    public void ClearOnlineRoundReplayRows()
    {
        foreach (RoundReplayRow row in spawnedRoundRows)
        {
            if (row != null)
                Destroy(row.gameObject);
        }

        spawnedRoundRows.Clear();
    }

    public void ShowReplayPreviewTiles(
    List<SimPlacedTileData> tiles)
    {
        ClearReplayPreviewTiles();

        if (tiles == null ||
            tiles.Count == 0)
        {
            return;
        }

        if (gameBoard == null)
        {
            Debug.LogWarning(
                "[REPLAY] Cannot show preview: gameBoard is null."
            );
            return;
        }

        GhostTile[] allGhostTiles =
            gameBoard.GetComponentsInChildren<GhostTile>(true);

        foreach (SimPlacedTileData tile in tiles)
        {
            if (tile == null)
                continue;

            GhostTile matchingGhostTile = null;

            foreach (GhostTile ghostTile in allGhostTiles)
            {
                if (ghostTile == null ||
                    ghostTile.letterPosition == null)
                {
                    continue;
                }

                if (ghostTile.letterPosition.RowX == tile.row &&
                    ghostTile.letterPosition.ColY == tile.col)
                {
                    matchingGhostTile = ghostTile;
                    break;
                }
            }

            if (matchingGhostTile == null)
            {
                Debug.LogWarning(
                    "[REPLAY] Preview could not find GhostTile at row " +
                    tile.row +
                    ", col " +
                    tile.col
                );
                continue;
            }

            GameObject preview =
                Instantiate(basicTile);

            preview.transform.SetParent(
                matchingGhostTile.transform,
                false
            );

            TileScript tileScript =
                preview.GetComponent<TileScript>();

            if (tileScript != null)
            {
                LetterInfo tileInfo =
                    new LetterInfo(
                        tile.letter,
                        tile.points
                    );

                tileInfo.bonusUsed = true;

                tileScript.InitTile(tileInfo);
                tileScript.SetLockedOnBoard(true);

                if (tileScript.PlacedTileData != null)
                {
                    tileScript.PlacedTileData.letterPosition =
                        new LetterPosition(
                            tile.row,
                            tile.col
                        );
                }
            }

            Image[] images =
                preview.GetComponentsInChildren<Image>(true);

            foreach (Image image in images)
            {
                if (image != null)
                {
                    Color original = image.color;

                    image.color = new Color(
                        replayPreviewColor.r,
                        replayPreviewColor.g,
                        replayPreviewColor.b,
                        original.a * replayPreviewColor.a
                    );
                }
            }

            replayPreviewTiles.Add(preview);
        }
    }

    public void ClearReplayPreviewTiles()
    {
        foreach (GameObject preview in replayPreviewTiles)
        {
            if (preview != null)
            {
                Destroy(preview);
            }
        }

        replayPreviewTiles.Clear();
    }
}