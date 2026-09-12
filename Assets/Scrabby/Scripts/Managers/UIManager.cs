
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

    [Header("Word outlines")]
    [SerializeField] private Sprite wordOutlineSprite;
    [SerializeField] private Sprite scoreBadgeSprite;
    [SerializeField] private TMP_FontAsset scoreBadgeFont;
    [SerializeField] private Color rejectedOutlineColour = new Color(0.84f, 0.15f, 0.16f, 1f);
    [SerializeField] private Color playedOutlineColour = new Color(0.18f, 0.64f, 0.33f, 1f);
    [SerializeField] private float rejectedOutlineSeconds = 0.55f;
    [SerializeField] private float rejectedOutlineHoldSeconds = 1.2f;
    [SerializeField] private float rejectedOutlineFadeOutSeconds = 0.45f;
    private GameObject rejectedOutline;
    private Coroutine rejectedOutlineAnimation;
    private GameObject playedOutline;
    private GameObject playedScoreBadge;
    private TextMeshProUGUI playedScoreLabel;
    private Coroutine playedOutlineAnimation;

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

    //[SerializeField] private GameObject replayPreviewTilePrefab;

    private readonly List<GameObject> replayPreviewTiles =
        new List<GameObject>();

    [SerializeField] private GameObject backToMatchButton;

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

    // The word's LetterInfo objects are the same instances the tiles carry, so
    // matching by reference picks out exactly the tiles that spelled it.
    public void HighlightRejectedWord(List<LetterInfo> word)
    {
        ClearHighlightsUnder(gameBoard);
        ClearHighlightsUnder(handTileHolder);
        HideRejectedOutlineNow();

        if (word == null || word.Count == 0 || gameBoard == null)
            return;

        List<TileScript> spelledIt = new List<TileScript>();

        foreach (TileScript tileScript in gameBoard.GetComponentsInChildren<TileScript>(true))
        {
            if (tileScript == null || tileScript.LetterInfo == null)
                continue;

            foreach (LetterInfo letter in word)
            {
                if (ReferenceEquals(letter, tileScript.LetterInfo))
                {
                    tileScript.SetInvalidHighlight(true);
                    spelledIt.Add(tileScript);
                    break;
                }
            }
        }

        DrawRejectedOutline(spelledIt);
    }

    // Boxes the word with a line that fills the gap between it and the cells
    // around it. A word is always a straight run, so one rectangle covers it.
    private void DrawRejectedOutline(List<TileScript> tiles)
    {
        if (tiles == null || tiles.Count == 0)
            return;

        List<RectTransform> cells = new List<RectTransform>();

        foreach (TileScript tile in tiles)
        {
            // A tile sits inside its board cell, and the cell is what the grid
            // positions, so the cell is the thing with a reliable rect.
            if (tile != null)
                cells.Add(tile.transform.parent as RectTransform);
        }

        RectTransform rect = PlaceOutline(
            ref rejectedOutline, "RejectedWordOutline", cells, rejectedOutlineColour);

        if (rect == null)
            return;

        if (rejectedOutlineAnimation != null)
            StopCoroutine(rejectedOutlineAnimation);

        rejectedOutlineAnimation = StartCoroutine(
            AnimateRejectedOutline(rect, rejectedOutline.GetComponent<Image>()));
    }

    // Boxes a set of board cells, with the line sitting in the gap around them.
    // Returns the outline's rect, or null if the cells could not be measured.
    // Both shapes are built in code rather than referenced as assets. They are
    // plain geometry, and a serialized reference has to survive every rename and
    // scene reload to work - which is exactly how this silently drew nothing.
    private static Sprite BuildRoundedRingSprite()
    {
        const int size = 160, ring = 15, radius = 34, border = 38;

        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;

        float half = size * 0.5f;
        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = x + 0.5f - half;
                float py = y + 0.5f - half;

                float outer = RoundedRectDistance(px, py, half, half, radius);
                float inner = RoundedRectDistance(
                    px, py, half - ring, half - ring, Mathf.Max(radius - ring, 1f));

                float alpha = Mathf.Clamp01(0.5f - outer) * Mathf.Clamp01(0.5f + inner);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();

        return Sprite.Create(
            texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(border, border, border, border));
    }

    private static Sprite BuildDiscSprite()
    {
        const int size = 128;

        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;

        float half = size * 0.5f;
        float radius = half - 1.5f;
        Color[] pixels = new Color[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Mathf.Sqrt(
                    (x + 0.5f - half) * (x + 0.5f - half) +
                    (y + 0.5f - half) * (y + 0.5f - half)) - radius;

                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - distance));
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();

        return Sprite.Create(
            texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    // Signed distance to a rounded rectangle centred on the origin.
    private static float RoundedRectDistance(
        float x, float y, float halfWidth, float halfHeight, float radius)
    {
        float qx = Mathf.Abs(x) - (halfWidth - radius);
        float qy = Mathf.Abs(y) - (halfHeight - radius);

        float outside = Mathf.Sqrt(
            Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) +
            Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));

        return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
    }

    private RectTransform PlaceOutline(
        ref GameObject outlineObject,
        string name,
        List<RectTransform> cells,
        Color colour)
    {
        if (cells == null || cells.Count == 0)
            return null;

        if (wordOutlineSprite == null)
            wordOutlineSprite = BuildRoundedRingSprite();

        RectTransform grid = null;
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        bool measured = false;

        foreach (RectTransform cell in cells)
        {
            if (cell == null)
                continue;

            if (grid == null)
                grid = cell.parent as RectTransform;

            Vector2 centre = cell.localPosition;
            Vector2 half = cell.rect.size * 0.5f;

            min = Vector2.Min(min, centre - half);
            max = Vector2.Max(max, centre + half);
            measured = true;
        }

        if (!measured || grid == null)
            return null;

        float gap = 15f;
        GridLayoutGroup layout = grid.GetComponent<GridLayoutGroup>();

        if (layout != null)
            gap = Mathf.Max(layout.spacing.x, layout.spacing.y);

        min -= new Vector2(gap, gap);
        max += new Vector2(gap, gap);

        if (outlineObject == null)
        {
            outlineObject = new GameObject(
                name,
                typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(LayoutElement));

            outlineObject.transform.SetParent(grid, false);

            // Without this the grid would treat the outline as an 82nd cell.
            outlineObject.GetComponent<LayoutElement>().ignoreLayout = true;

            Image created = outlineObject.GetComponent<Image>();
            created.sprite = wordOutlineSprite;
            created.type = Image.Type.Sliced;
            created.fillCenter = false;
            created.raycastTarget = false;
        }

        RectTransform rect = outlineObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = max - min;
        rect.anchoredPosition = (min + max) * 0.5f;
        rect.SetAsLastSibling();

        outlineObject.GetComponent<Image>().color = colour;
        outlineObject.SetActive(true);

        return rect;
    }

    private IEnumerator AnimateRejectedOutline(RectTransform rect, Image outline)
    {
        float elapsed = 0f;

        while (elapsed < rejectedOutlineSeconds)
        {
            elapsed += Time.deltaTime;

            float progress = Mathf.Clamp01(elapsed / rejectedOutlineSeconds);
            float eased = 1f - Mathf.Pow(1f - progress, 3f);

            outline.color = new Color(
                rejectedOutlineColour.r,
                rejectedOutlineColour.g,
                rejectedOutlineColour.b,
                eased);

            float overshoot = Mathf.Lerp(1.05f, 1f, eased);
            rect.localScale = new Vector3(overshoot, overshoot, 1f);

            yield return null;
        }

        outline.color = rejectedOutlineColour;
        rect.localScale = Vector3.one;
        rejectedOutlineAnimation = null;
    }

    public void ClearRejectedWordHighlight()
    {
        // Tiles move back to the hand after a failed turn, so clear both places.
        ClearHighlightsUnder(gameBoard);
        ClearHighlightsUnder(handTileHolder);

        if (rejectedOutline == null || !rejectedOutline.activeSelf)
            return;

        // The tiles leave straight away, but snapping the box off with them reads
        // as a glitch. Let it sit on the empty cells a moment, then fade.
        if (rejectedOutlineAnimation != null)
            StopCoroutine(rejectedOutlineAnimation);

        rejectedOutlineAnimation = StartCoroutine(FadeRejectedOutlineOut());
    }

    private IEnumerator FadeRejectedOutlineOut()
    {
        Image outline = rejectedOutline.GetComponent<Image>();
        outline.color = rejectedOutlineColour;

        yield return new WaitForSeconds(rejectedOutlineHoldSeconds);

        float elapsed = 0f;

        while (elapsed < rejectedOutlineFadeOutSeconds)
        {
            elapsed += Time.deltaTime;

            float progress = Mathf.Clamp01(elapsed / rejectedOutlineFadeOutSeconds);

            outline.color = new Color(
                rejectedOutlineColour.r,
                rejectedOutlineColour.g,
                rejectedOutlineColour.b,
                1f - progress);

            yield return null;
        }

        rejectedOutline.SetActive(false);
        rejectedOutlineAnimation = null;
    }

    // Boxes the word that was actually played and pins its score to the corner
    // of that box, so the number belongs to a word instead of floating loose.
    public void HighlightPlayedWord(List<LetterPosition> cells, int score)
    {
        HidePlayedWordHighlight();

        if (cells == null || cells.Count == 0 || gameBoard == null)
            return;

        List<RectTransform> cellRects = new List<RectTransform>();

        foreach (LetterPosition position in cells)
        {
            GhostTile cell = FindGhostTileByLetterPosition(position);

            if (cell != null)
                cellRects.Add(cell.transform as RectTransform);
        }

        RectTransform rect = PlaceOutline(
            ref playedOutline, "PlayedWordOutline", cellRects, playedOutlineColour);

        if (rect == null)
            return;

        AttachScoreBadge(rect, score);

        if (playedOutlineAnimation != null)
            StopCoroutine(playedOutlineAnimation);

        playedOutlineAnimation = StartCoroutine(AnimatePlayedOutline(rect));
    }

    private void AttachScoreBadge(RectTransform outlineRect, int score)
    {
        if (scoreBadgeSprite == null)
            scoreBadgeSprite = BuildDiscSprite();

        if (playedScoreBadge == null)
        {
            playedScoreBadge = new GameObject(
                "PlayedWordScore",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));

            Image badge = playedScoreBadge.GetComponent<Image>();
            badge.sprite = scoreBadgeSprite;
            badge.raycastTarget = false;

            GameObject label = new GameObject(
                "Score", typeof(RectTransform), typeof(CanvasRenderer));
            label.transform.SetParent(playedScoreBadge.transform, false);

            playedScoreLabel = label.AddComponent<TextMeshProUGUI>();
            playedScoreLabel.alignment = TextAlignmentOptions.Center;
            playedScoreLabel.enableAutoSizing = true;
            playedScoreLabel.fontSizeMin = 8f;
            playedScoreLabel.fontSizeMax = 40f;
            playedScoreLabel.fontStyle = FontStyles.Bold;
            playedScoreLabel.color = Color.white;
            playedScoreLabel.raycastTarget = false;

            // Borrow the round message's font if none was assigned, so the badge
            // matches the rest of the UI without needing a wired reference.
            if (scoreBadgeFont == null && roundMessageText != null)
                scoreBadgeFont = roundMessageText.font;

            if (scoreBadgeFont != null)
                playedScoreLabel.font = scoreBadgeFont;

            RectTransform labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(6f, 6f);
            labelRect.offsetMax = new Vector2(-6f, -6f);
        }

        // Parented to the outline so it travels with the box rather than being
        // positioned against the board separately.
        playedScoreBadge.transform.SetParent(outlineRect, false);

        float diameter = Mathf.Min(outlineRect.rect.width, outlineRect.rect.height) * 0.62f;

        RectTransform badgeRect = playedScoreBadge.GetComponent<RectTransform>();
        badgeRect.anchorMin = new Vector2(1f, 0f);      // bottom-right, the end of the word
        badgeRect.anchorMax = new Vector2(1f, 0f);
        badgeRect.pivot = new Vector2(0.5f, 0.5f);
        badgeRect.anchoredPosition = Vector2.zero;      // centred on the corner of the line
        badgeRect.sizeDelta = new Vector2(diameter, diameter);
        badgeRect.localScale = Vector3.one;

        playedScoreBadge.GetComponent<Image>().color = playedOutlineColour;

        if (playedScoreLabel != null)
            playedScoreLabel.text = score.ToString();

        playedScoreBadge.SetActive(true);
    }

    private IEnumerator AnimatePlayedOutline(RectTransform rect)
    {
        Image outline = playedOutline.GetComponent<Image>();
        Image badge = playedScoreBadge != null ? playedScoreBadge.GetComponent<Image>() : null;

        float elapsed = 0f;

        while (elapsed < rejectedOutlineSeconds)
        {
            elapsed += Time.deltaTime;

            float progress = Mathf.Clamp01(elapsed / rejectedOutlineSeconds);
            float eased = 1f - Mathf.Pow(1f - progress, 3f);

            outline.color = new Color(
                playedOutlineColour.r, playedOutlineColour.g, playedOutlineColour.b, eased);

            if (badge != null)
            {
                badge.color = new Color(
                    playedOutlineColour.r, playedOutlineColour.g, playedOutlineColour.b, eased);

                if (playedScoreLabel != null)
                    playedScoreLabel.alpha = eased;

                // The badge lands a beat after the box it hangs off.
                float pop = Mathf.Clamp01((progress - 0.35f) / 0.65f);
                float settle = Mathf.Lerp(0.4f, 1f, 1f - Mathf.Pow(1f - pop, 3f));
                playedScoreBadge.transform.localScale = new Vector3(settle, settle, 1f);
            }

            float overshoot = Mathf.Lerp(1.05f, 1f, eased);
            rect.localScale = new Vector3(overshoot, overshoot, 1f);

            yield return null;
        }

        outline.color = playedOutlineColour;
        rect.localScale = Vector3.one;

        if (badge != null)
        {
            badge.color = playedOutlineColour;
            playedScoreBadge.transform.localScale = Vector3.one;

            if (playedScoreLabel != null)
                playedScoreLabel.alpha = 1f;
        }

        playedOutlineAnimation = null;
    }

    public void HidePlayedWordHighlight()
    {
        if (playedOutlineAnimation != null)
        {
            StopCoroutine(playedOutlineAnimation);
            playedOutlineAnimation = null;
        }

        if (playedScoreBadge != null)
            playedScoreBadge.SetActive(false);

        if (playedOutline != null)
            playedOutline.SetActive(false);
    }

    private void HideRejectedOutlineNow()
    {
        if (rejectedOutlineAnimation != null)
        {
            StopCoroutine(rejectedOutlineAnimation);
            rejectedOutlineAnimation = null;
        }

        if (rejectedOutline != null)
            rejectedOutline.SetActive(false);
    }

    private void ClearHighlightsUnder(GameObject root)
    {
        if (root == null)
            return;

        foreach (TileScript tileScript in root.GetComponentsInChildren<TileScript>(true))
        {
            if (tileScript != null)
                tileScript.SetInvalidHighlight(false);
        }
    }

    public void ReturnTilesToHand()
    {
        ClearRejectedWordHighlight();

        // The tiles are leaving the board, so the box round them goes with them.
        // ApplyWinningMove draws a fresh one for whichever word actually lands.
        HidePlayedWordHighlight();

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
                ghostTile.FitChildToCell(tempTile.transform);

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
            ghostTile.FitChildToCell(tempTile.transform);

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

        bool isOnlineMatch =
        Singleton.Instance != null &&
        Singleton.Instance.GameLogic != null &&
        Singleton.Instance.GameLogic.IsOnlineMatch;

        Debug.Log(
            "[GAME OVER] isOnlineMatch=" + isOnlineMatch +
            " | backToMatchButton=" +
            (backToMatchButton != null ? backToMatchButton.name : "NULL")
        );

        if (backToMatchButton != null)
        {
            backToMatchButton.SetActive(isOnlineMatch);

            Debug.Log(
            "[GAME OVER] Back button active after SetActive: " +
            backToMatchButton.activeSelf);
        }
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

            matchingGhostTile.FitChildToCell(preview.transform);

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