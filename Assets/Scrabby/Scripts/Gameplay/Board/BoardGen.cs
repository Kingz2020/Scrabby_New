using UnityEngine;
using UnityEngine.UI;

// Coordinate convention used everywhere:
// x = horizontal board coordinate / column, 1-based for LetterPosition.
// y = vertical board coordinate / row, 1-based for LetterPosition.
// LetterPosition.RowX = x; LetterPosition.ColY = y.
// Board arrays use [x, y].
// Bonus arrays are 0-based: boardBonusTiles[x - 1, y - 1].

public class BoardGen : MonoBehaviour {

    public GameObject GhostGO;
    public int RowX;
    public int RowY;

    [Header("Proportions, as a fraction of the board's width")]

    [SerializeField] private float sidePadding = 0.022f;
    [SerializeField] private float topPadding = 0.012f;
    [SerializeField] private float cellGap = 0.0152f;

    private GridLayoutGroup grid;
    private RectTransform rect;
    private float lastWidth = -1f;

    public void Start() {
        for (int y = 1; y <= RowY; y++) {
            for (int x = 1; x <= RowX; x++) {
                GameObject goTemp = Instantiate(GhostGO, transform);
                goTemp.GetComponent<GhostTile>().SetLocation(x, y);
            }
        }

        FitToWidth();
    }

    private void OnRectTransformDimensionsChange() {
        FitToWidth();
    }

    // Each cell draws its own cavity, so the grid owes nothing to a background
    // image and can simply divide up whatever width it is handed. That is what
    // makes the board survive an aspect ratio it was never designed against.
    private void FitToWidth() {
        if (grid == null) grid = GetComponent<GridLayoutGroup>();
        if (rect == null) rect = transform as RectTransform;
        if (grid == null || rect == null || RowX <= 0 || RowY <= 0) return;

        float width = rect.rect.width;
        if (width <= 0f || Mathf.Approximately(width, lastWidth)) return;
        lastWidth = width;

        float padX = width * sidePadding;
        float padY = width * topPadding;
        float gap = width * cellGap;

        float cell = (width - padX * 2f - gap * (RowX - 1)) / RowX;
        if (cell <= 0f) return;

        grid.padding = new RectOffset(
            Mathf.RoundToInt(padX), Mathf.RoundToInt(padX),
            Mathf.RoundToInt(padY), Mathf.RoundToInt(padY));
        grid.cellSize = new Vector2(cell, cell);
        grid.spacing = new Vector2(gap, gap);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = RowX;

        // GetStartOffset drops the whole surplus at the far edge, so with the
        // default UpperLeft every rounding error piles up below the last row and
        // the board reads lop-sided. Centred, any residual splits evenly instead.
        grid.childAlignment = TextAnchor.MiddleCenter;

        float height = padY * 2f + cell * RowY + gap * (RowY - 1);
        rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);

        ScrabbyLog.Trace($"[BOARD] width={width:F2} cell={cell:F2} gap={gap:F2} " +
                  $"padX={padX:F2} padY={padY:F2} height={height:F2} " +
                  $"rectAfter={rect.rect.width:F2}x{rect.rect.height:F2}");
    }
}
