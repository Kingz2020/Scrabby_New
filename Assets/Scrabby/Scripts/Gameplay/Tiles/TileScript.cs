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

    public void OnEndDrag(PointerEventData eventData)
    {
        if (isLockedOnBoard)
            return;

        //background.raycastTarget = true;
        canvasGroup.blocksRaycasts = true;
        Singleton.Instance.DropManager.isCurrentlyDragging = false;

        GhostTile targetLocation = Singleton.Instance.DropManager.GetCurrentLocation();

        if (targetLocation == null)
        {
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

        targetLocation.ResetVisuals();

        placedTile.letterPosition = targetLocation.letterPosition;

        transform.SetParent(targetLocation.transform);
        transform.localPosition = Vector3.zero;

        Singleton.Instance.DropManager.SetTempGrabbedTile(placedTile);
        Singleton.Instance.DropManager.AddLocation();
        Singleton.Instance.DropManager.ClearCurrentLocation(targetLocation);
    }
    public IEnumerator PlayWinningReplayDrop(
    float duration,
    Color highlightColor,
    float dropHeightMultiplier = 0.5f)
    {
        if (this == null || gameObject == null)
            yield break;

        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        RectTransform rt = transform as RectTransform;
        float tileHeight = rt != null ? rt.rect.height : 90f;
        float dropHeight = tileHeight * dropHeightMultiplier;

        Vector3 finalPosition = transform.localPosition;
        Vector3 startPosition = finalPosition + Vector3.up * dropHeight;

        Color originalLetterColor = textLetter != null
            ? textLetter.color
            : Color.white;

        FontStyles originalFontStyle = textLetter != null
            ? textLetter.fontStyle
            : FontStyles.Normal;

        if (textLetter != null)
        {
            textLetter.color = highlightColor;
            textLetter.fontStyle = FontStyles.Bold;
        }

        canvasGroup.alpha = 0f;
        transform.localPosition = startPosition;

        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (this == null || gameObject == null)
                yield break;

            elapsed += Time.unscaledDeltaTime;

            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);

            transform.localPosition =
                Vector3.Lerp(startPosition, finalPosition, eased);

            canvasGroup.alpha = t;

            yield return null;
        }

        if (this == null || gameObject == null)
            yield break;

        transform.localPosition = finalPosition;
        canvasGroup.alpha = 1f;

        yield return new WaitForSecondsRealtime(0.25f);

        if (this == null || gameObject == null)
            yield break;

        if (textLetter != null)
        {
            textLetter.color = originalLetterColor;
            textLetter.fontStyle = originalFontStyle;
        }
    }
}