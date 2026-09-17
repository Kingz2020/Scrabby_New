using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Coordinate convention used everywhere:
// x = horizontal board coordinate / column, 1-based for LetterPosition.
// y = vertical board coordinate / row, 1-based for LetterPosition.
// LetterPosition.RowX = x; LetterPosition.ColY = y.
// Board arrays use [x, y].
// Bonus arrays are 0-based: boardBonusTiles[x - 1, y - 1].

public class GhostTile : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private UnityEngine.UI.Image border;
    [SerializeField] private UnityEngine.UI.Image donut;

    [Header("Glow Styling")]

    [SerializeField] private Color normalBorderColor = new Color(1f, 1f, 1f, 0.12f);
    [SerializeField] private Color hoverBorderColor = new Color(1f, 0.82f, 0.18f, 1f);
    [SerializeField] private Vector3 normalScale = Vector3.one;
    [SerializeField] private Vector3 hoverScale = new Vector3(1.10f, 1.10f, 1f);

    public LetterPosition letterPosition = new LetterPosition();

    private void Awake()
    {
        if (border == null)
        {
            Transform borderTrans = transform.Find("border");
            if (borderTrans != null)
            {
                border = borderTrans.GetComponent<UnityEngine.UI.Image>();
            }
        }

        if (donut == null)
        {
            Transform donutTrans = transform.Find("Donut");
            if (donutTrans != null)
            {
                donut = donutTrans.GetComponent<UnityEngine.UI.Image>();
            }
        }

        if (border != null)
        {
            border.color = normalBorderColor;
            border.transform.localScale = normalScale;
        }

        if (donut != null)
        {
            Color c = donut.color;
            c.a = 0.08f;
            donut.color = c;
            donut.transform.localScale = normalScale;
        }
    }

    public void SetLocation(int x, int y)
    {
        letterPosition.RowX = x;
        letterPosition.ColY = y;
        ScrabbyLog.Trace($"[GHOST] {name} SetLocation => RowX={x}, ColY={y}");
    }

    // The grid sizes each cell to match the board art's cavity, but a child
    // parented in keeps whatever size its prefab was authored at. Stretching it
    // to the cell is what makes every tile the same size and flush in the cavity.
    public void FitChildToCell(Transform child)
    {
        RectTransform rt = child as RectTransform;

        if (rt == null)
            return;

        rt.localRotation = Quaternion.identity;
        rt.localScale = Vector3.one;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    public void ResetVisuals()
    {
        if (border != null)
        {
            border.color = normalBorderColor;
            border.transform.localScale = normalScale;
        }

        if (donut != null)
        {
            Color c = donut.color;
            c.a = 0.08f;
            donut.color = c;
            donut.transform.localScale = normalScale;
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (Singleton.Instance == null || Singleton.Instance.DropManager == null)
        {
            Debug.LogWarning("GhostTile.OnPointerEnter: Singleton or DropManager is null on " + gameObject.name);
            return;
        }

        if (Singleton.Instance.DropManager.isCurrentlyDragging)
        {
            if (donut != null)
            {
                donut.color = hoverBorderColor;
                donut.transform.localScale = hoverScale;
                donut.transform.SetAsLastSibling();
            }

            Singleton.Instance.DropManager.SetCurrentLocation(this);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        ResetVisuals();

        if (Singleton.Instance == null || Singleton.Instance.DropManager == null)
        {
            Debug.LogWarning("GhostTile.OnPointerExit: Singleton or DropManager is null on " + gameObject.name);
            return;
        }

        Singleton.Instance.DropManager.ClearCurrentLocation(this);
    }
}