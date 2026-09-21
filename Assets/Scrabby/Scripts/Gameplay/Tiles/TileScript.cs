using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections;

public class TileScript : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public Image background;
    public TextMeshProUGUI textLetter;
    public TextMeshProUGUI textPoints;
    private CanvasGroup canvasGroup;

    [SerializeField] private PlacedTile placedTile;

    private Vector3 origin;
    private bool snapTileBack;
    private Transform originalParent;
    private Vector3 dragOffset;
    public PlacedTile PlacedTileData => placedTile;
    public LetterInfo LetterInfo => placedTile != null ? placedTile.letterInfo : null;
    public LetterPosition LetterPosition => placedTile != null ? placedTile.letterPosition : null;

    [SerializeField] private bool isLockedOnBoard = false;

    private static readonly Color InvalidWordColour = new Color(0.84f, 0.15f, 0.16f, 1f);
    private Color normalLetterColour;
    private Color normalPointsColour;
    private FontStyles normalFontStyle;
    private bool normalColoursCaptured;

    // Both the rejected-word highlight and the replay highlight paint over the
    // tile's normal look, so they share one record of what normal was.
    private void CaptureNormalColours()
    {
        if (normalColoursCaptured || textLetter == null)
            return;

        normalLetterColour = textLetter.color;
        normalPointsColour = textPoints != null ? textPoints.color : normalLetterColour;
        normalFontStyle = textLetter.fontStyle;
        normalColoursCaptured = true;
    }

    // Marks this tile as part of a word the dictionary rejected.
    public void SetInvalidHighlight(bool invalid)
    {
        if (textLetter == null)
            return;

        CaptureNormalColours();

        textLetter.color = invalid ? InvalidWordColour : normalLetterColour;

        if (textPoints != null)
            textPoints.color = invalid ? InvalidWordColour : normalPointsColour;
    }

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    public void InitTile(LetterInfo tileInfo)
    {
        if (placedTile == null)
            placedTile = new PlacedTile();

        placedTile.letterInfo = tileInfo;
        textLetter.text = placedTile.letterInfo.letter;
        textPoints.text = placedTile.letterInfo.points.ToString();
    }

    public void SetLockedOnBoard(bool locked)
    {
        isLockedOnBoard = locked;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (isLockedOnBoard)
            return;

        canvasGroup.blocksRaycasts = false;
        origin = transform.position;
        originalParent = transform.parent;

        dragOffset = transform.position - (Vector3)eventData.position;

        Singleton.Instance.DropManager.ForgetWhereThePointerWas();
        Singleton.Instance.DropManager.isCurrentlyDragging = true;
        Singleton.Instance.DropManager.SetTempGrabbedTile(placedTile);

        snapTileBack = Singleton.Instance.DropManager.RemovedPlacedTile(placedTile);

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            transform.SetParent(canvas.transform, true);
            transform.SetAsLastSibling();
        }
    }


    public void OnDrag(PointerEventData eventData)
    {
        if (isLockedOnBoard)
            return;

        transform.position = (Vector3)eventData.position + dragOffset;
    }

    // Whether the pointer let go over the rack. Tested against the rack's own
    // rectangle rather than what the raycast hit, so it works wherever on the
    // rack the tile is dropped - over a gap between tiles, or over the rack's
    // background.
    private bool DroppedOverTheRack(PointerEventData eventData)
    {
        GameObject hand = Singleton.Instance != null &&
                          Singleton.Instance.UIManager != null
            ? Singleton.Instance.UIManager.handTileHolder
            : null;

        if (hand == null)
            return false;

        RectTransform rect = hand.transform as RectTransform;

        if (rect == null)
            return false;

        Canvas canvas = GetComponentInParent<Canvas>();

        // A Screen Space Overlay canvas wants a null camera here; anything else
        // wants the one it renders through.
        Camera camera = canvas != null &&
                        canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;

        return RectTransformUtility.RectangleContainsScreenPoint(
            rect, eventData.position, camera);
    }

    private void ReturnThisTileToHand()
    {
        GameObject hand = Singleton.Instance.UIManager.handTileHolder;

        transform.SetParent(hand.transform, false);
        transform.localPosition = Vector3.zero;
        transform.localScale = Vector3.one;

        // It is in the hand now, so it is not anywhere on the board.
        if (placedTile != null)
            placedTile.letterPosition = null;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (isLockedOnBoard)
            return;

        //background.raycastTarget = true;
        canvasGroup.blocksRaycasts = true;
        Singleton.Instance.DropManager.isCurrentlyDragging = false;

        // Asked before the board is consulted, because it always answers.
        // GetCurrentLocation falls back to the last cell the tile passed over,
        // so letting go anywhere off the board - the rack included - put the
        // tile back on that square. Taking a single tile back was impossible,
        // leaving the return-everything button as the only way to change your
        // mind about one of them.
        //
        // Nothing has to be told the tile has left the board: OnBeginDrag
        // already took it off, and this simply declines to put it back.
        if (DroppedOverTheRack(eventData))
        {
            ReturnThisTileToHand();
            Singleton.Instance.DropManager.SetTempGrabbedTile(null);
            return;
        }

        GhostTile targetLocation = Singleton.Instance.DropManager.GetCurrentLocation();

        if (targetLocation == null)
        {
            // Said out loud, because "the tile would not go down" is a report
            // that is otherwise impossible to act on.
            Debug.Log("[DRAG] " + LetterForLog() + " let go with no square under it; back it goes.");

            transform.SetParent(originalParent);
            transform.position = origin;

            if (snapTileBack)
            {
                Singleton.Instance.DropManager.SetTempGrabbedTile(placedTile);
                Singleton.Instance.DropManager.AddLocation();
            }

            return;
        }

        TileScript existingTile = null;

        foreach (Transform child in targetLocation.transform)
        {
            TileScript childTile = child.GetComponent<TileScript>();
            if (childTile != null && childTile != this)
            {
                existingTile = childTile;
                break;
            }
        }

        if (existingTile != null)
        {
            Debug.Log("[DRAG] " + LetterForLog() + " let go over " +
                      targetLocation.letterPosition.RowX + "," + targetLocation.letterPosition.ColY +
                      ", which already has " + (existingTile.LetterInfo != null
                          ? existingTile.LetterInfo.letter : "a tile") + "; back it goes.");

            transform.SetParent(originalParent);
            transform.position = origin;

            if (snapTileBack)
            {
                Singleton.Instance.DropManager.SetTempGrabbedTile(placedTile);
                Singleton.Instance.DropManager.AddLocation();
            }

            Singleton.Instance.DropManager.ClearCurrentLocation(targetLocation);
            return;
        }

        PlaceOnCell(targetLocation);
    }

    // A tile landing on a square, whoever gave the instruction - a player's
    // finger, or the tutorial's hand. Both go through here, so a tutorial
    // placement cannot behave differently from a real one.
    public void PlaceOnCell(GhostTile targetLocation)
    {
        if (targetLocation == null)
            return;

        canvasGroup.blocksRaycasts = true;

        targetLocation.ResetVisuals();

        placedTile.letterPosition = targetLocation.letterPosition;

        transform.SetParent(targetLocation.transform);
        targetLocation.FitChildToCell(transform);

        Singleton.Instance.DropManager.SetTempGrabbedTile(placedTile);
        Singleton.Instance.DropManager.AddLocation();
        Singleton.Instance.DropManager.ClearCurrentLocation(targetLocation);

        // The tile has landed on a square, which is the only case here that
        // deserves a sound: the others put it back where it came from.
        Sound.Play(Sound.TileDown);
    }

    private string LetterForLog()
    {
        return placedTile != null && placedTile.letterInfo != null
            ? placedTile.letterInfo.letter
            : "A tile";
    }

    // The tutorial picking a tile up: everything OnBeginDrag does except the
    // pointer, so the tile can then be carried by whatever is moving it.
    public void BeginDemoDrag()
    {
        if (Singleton.Instance == null || Singleton.Instance.DropManager == null)
            return;

        canvasGroup.blocksRaycasts = false;
        origin = transform.position;
        originalParent = transform.parent;

        Singleton.Instance.DropManager.ForgetWhereThePointerWas();
        Singleton.Instance.DropManager.isCurrentlyDragging = true;
        Singleton.Instance.DropManager.SetTempGrabbedTile(placedTile);

        snapTileBack = Singleton.Instance.DropManager.RemovedPlacedTile(placedTile);

        Canvas canvas = GetComponentInParent<Canvas>();

        if (canvas != null)
        {
            transform.SetParent(canvas.transform, true);
            transform.SetAsLastSibling();
        }
    }

    public void EndDemoDrag(GhostTile targetLocation)
    {
        if (Singleton.Instance != null && Singleton.Instance.DropManager != null)
            Singleton.Instance.DropManager.isCurrentlyDragging = false;

        PlaceOnCell(targetLocation);
    }
    // ---- Round replay -----------------------------------------------------
    // A tile knows how to fall, land, punch and leave. It deliberately does not
    // know how long to wait between those, or when its highlight should go back
    // to normal: the word owns that, so a colour can survive a whole word rather
    // than reverting the moment each letter lands.

    // Landing squash. Wider and shorter for an instant, then sprung back.
    private static readonly Vector3 LandingSquash = new Vector3(1.12f, 0.84f, 1f);

    private void EnsureCanvasGroup()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    // Overshoots 1 slightly before settling, which is what reads as springiness.
    private static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;

        float p = t - 1f;
        return 1f + c3 * p * p * p + c1 * p * p;
    }

    // Called the instant a replay tile is created, so it never flashes onto the
    // board at full opacity before its drop begins.
    public void HideForReplayDrop()
    {
        EnsureCanvasGroup();
        canvasGroup.alpha = 0f;
    }

    public void SetReplayHighlight(Color highlightColour)
    {
        if (textLetter == null)
            return;

        CaptureNormalColours();

        textLetter.color = highlightColour;
        textLetter.fontStyle = FontStyles.Bold;
    }

    public void ClearReplayHighlight()
    {
        if (textLetter == null || !normalColoursCaptured)
            return;

        textLetter.color = normalLetterColour;
        textLetter.fontStyle = normalFontStyle;
    }

    // Falls from above the cell and lands. Under gravity a tile is fastest when
    // it lands, so the fall accelerates rather than easing out into place.
    public IEnumerator PlayReplayDrop(
        float fallSeconds,
        float settleSeconds,
        float dropHeightMultiplier = 0.75f)
    {
        if (this == null || gameObject == null)
            yield break;

        EnsureCanvasGroup();

        RectTransform rt = transform as RectTransform;
        float tileHeight = rt != null ? rt.rect.height : 90f;
        float dropHeight = tileHeight * dropHeightMultiplier;

        Vector3 restPosition = transform.localPosition;
        Vector3 startPosition = restPosition + Vector3.up * dropHeight;

        canvasGroup.alpha = 0f;
        transform.localPosition = startPosition;
        transform.localScale = Vector3.one;

        float elapsed = 0f;

        while (elapsed < fallSeconds)
        {
            if (this == null || gameObject == null)
                yield break;

            elapsed += Time.unscaledDeltaTime;

            float t = Mathf.Clamp01(elapsed / fallSeconds);

            transform.localPosition =
                Vector3.Lerp(startPosition, restPosition, t * t);

            // Up to full opacity well before impact, so the tile is legible as
            // it falls instead of arriving and then appearing.
            canvasGroup.alpha = Mathf.Clamp01(t * 2.2f);

            yield return null;
        }

        if (this == null || gameObject == null)
            yield break;

        transform.localPosition = restPosition;
        canvasGroup.alpha = 1f;

        elapsed = 0f;

        while (elapsed < settleSeconds)
        {
            if (this == null || gameObject == null)
                yield break;

            elapsed += Time.unscaledDeltaTime;

            float t = Mathf.Clamp01(elapsed / settleSeconds);

            transform.localScale =
                Vector3.LerpUnclamped(LandingSquash, Vector3.one, EaseOutBack(t));

            yield return null;
        }

        if (this == null || gameObject == null)
            yield break;

        transform.localScale = Vector3.one;
    }

    // One pulse, run across every tile of a finished word so the word reads as
    // a word rather than as a row of separate letters.
    public IEnumerator PlayReplayPunch(float seconds, float strength = 0.14f)
    {
        if (this == null || gameObject == null)
            yield break;

        float elapsed = 0f;

        while (elapsed < seconds)
        {
            if (this == null || gameObject == null)
                yield break;

            elapsed += Time.unscaledDeltaTime;

            float t = Mathf.Clamp01(elapsed / seconds);
            float scale = 1f + strength * Mathf.Sin(t * Mathf.PI);

            transform.localScale = new Vector3(scale, scale, 1f);

            yield return null;
        }

        if (this == null || gameObject == null)
            yield break;

        transform.localScale = Vector3.one;
    }

    // Shrinks away instead of being destroyed mid-frame, so a word that is only
    // being shown has a visible end rather than blinking out.
    public IEnumerator PlayReplayExit(float seconds)
    {
        if (this == null || gameObject == null)
            yield break;

        EnsureCanvasGroup();

        float startAlpha = canvasGroup.alpha;
        float elapsed = 0f;

        while (elapsed < seconds)
        {
            if (this == null || gameObject == null)
                yield break;

            elapsed += Time.unscaledDeltaTime;

            float t = Mathf.Clamp01(elapsed / seconds);
            float eased = t * t;

            canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, eased);

            float scale = Mathf.Lerp(1f, 0.72f, eased);
            transform.localScale = new Vector3(scale, scale, 1f);

            yield return null;
        }

        if (this == null || gameObject == null)
            yield break;

        canvasGroup.alpha = 0f;
    }
}
