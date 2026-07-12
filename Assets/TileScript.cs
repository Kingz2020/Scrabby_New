using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class TileScript : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public Image background;
    public TextMeshProUGUI textLetter;
    public TextMeshProUGUI textPoints;

    [SerializeField] private PlacedTile placedTile;

    private Vector3 origin;
    private bool snapTileBack;
    private Transform originalParent;
    private Vector3 dragOffset;
    public PlacedTile PlacedTileData => placedTile;
    public LetterInfo LetterInfo => placedTile != null ? placedTile.letterInfo : null;
    public LetterPosition LetterPosition => placedTile != null ? placedTile.letterPosition : null;

    [SerializeField] private bool isLockedOnBoard = false;

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

        background.raycastTarget = false;
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

        background.raycastTarget = true;
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
}