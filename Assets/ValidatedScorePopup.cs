using System.Collections;
using UnityEngine;

public class ValidatedScorePopup : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private RectTransform rectTransform;
    public Vector2 floatOffset = new Vector2(40f, 40f);
    public float fadeDuration = 1.5f;

    public void Play(float lifetime)
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        if (rectTransform == null)
            rectTransform = GetComponent<RectTransform>();

        StartCoroutine(AnimatePopup(lifetime));
    }

    private IEnumerator AnimatePopup(float lifetime)
    {
        if (canvasGroup != null)
            canvasGroup.alpha = 1f;

        Vector2 startPos = rectTransform != null ? rectTransform.anchoredPosition : Vector2.zero;
        Vector2 endPos = startPos + floatOffset;

        float elapsed = 0f;

        while (elapsed < lifetime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / lifetime);

            if (rectTransform != null)
                rectTransform.anchoredPosition = Vector2.Lerp(startPos, endPos, t);

            if (canvasGroup != null)
            {
                float fadeStartTime = Mathf.Max(0f, lifetime - fadeDuration);

                if (elapsed >= fadeStartTime)
                {
                    float fadeT = Mathf.InverseLerp(fadeStartTime, lifetime, elapsed);
                    canvasGroup.alpha = Mathf.Lerp(1f, 0f, fadeT);
                }
            }

            yield return null;
        }

        Destroy(gameObject);
    }
}