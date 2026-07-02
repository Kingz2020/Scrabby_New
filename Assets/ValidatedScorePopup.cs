using System.Collections;
using UnityEngine;

public class ValidatedScorePopup : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private RectTransform rectTransform;
    [SerializeField] private Vector2 floatOffset = new Vector2(60f, 60f);
    [SerializeField] private float fadeDuration = 0.75f;

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