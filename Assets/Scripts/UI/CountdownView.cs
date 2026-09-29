using TMPro;
using UnityEngine;

/// <summary>
/// Big centered 3 / 2 / 1 / GO! text with a scale punch and quick fade-in. Runs on unscaled time
/// because the match is frozen (timeScale 0) during the countdown. GameManager decides what to show
/// and when; this only animates it.
/// </summary>
public class CountdownView : MonoBehaviour
{
    [SerializeField] private PongUITheme theme;
    [SerializeField] private TMP_Text label;

    [Header("Animation")]
    [SerializeField] private float punchScale = 1.9f;
    [SerializeField] private float punchSeconds = 0.28f;
    [SerializeField] private float fadeInSeconds = 0.08f;
    [Tooltip("GO! settles slightly larger than the numbers.")]
    [SerializeField] private float goScale = 1.3f;
    [SerializeField] private float goPunchScale = 2.2f;
    [Tooltip("How long GO! stays visible (the ball is already served).")]
    [SerializeField] private float goSeconds = 0.5f;

    private float shownAt;
    private float hideAt = -1f;
    private float fromScale;
    private float restScale = 1f;
    private float baseAlpha = 1f;

    private void Awake()
    {
        Hide();
    }

    public void ShowNumber(int number)
    {
        Color color = Color.white;
        if (theme != null)
            color = number == 3 ? theme.primary : number == 2 ? theme.secondary : theme.highlight;
        Show(number.ToString(), color, -1f, punchScale, 1f);
    }

    public void ShowGo()
    {
        Show("GO!", theme != null ? theme.highlight : Color.yellow, goSeconds, goPunchScale, goScale);
    }

    public void Hide()
    {
        hideAt = -1f;
        label.gameObject.SetActive(false);
    }

    private void Show(string text, Color color, float duration, float punch, float rest)
    {
        label.text = text;
        label.color = color;
        baseAlpha = color.a;
        label.alpha = 0f;
        label.gameObject.SetActive(true);
        shownAt = Time.unscaledTime;
        hideAt = duration > 0f ? shownAt + duration : -1f;
        fromScale = punch;
        restScale = rest;
        label.transform.localScale = Vector3.one * punch;
    }

    private void Update()
    {
        if (!label.gameObject.activeSelf) return;

        float since = Time.unscaledTime - shownAt;
        float t = Mathf.Clamp01(since / Mathf.Max(0.01f, punchSeconds));
        float eased = 1f - (1f - t) * (1f - t) * (1f - t);   // ease-out cubic
        label.transform.localScale = Vector3.one * Mathf.Lerp(fromScale, restScale, eased);

        float alpha = baseAlpha * Mathf.Clamp01(since / Mathf.Max(0.01f, fadeInSeconds));
        if (hideAt > 0f)
        {
            float remaining = hideAt - Time.unscaledTime;
            alpha *= Mathf.Clamp01(remaining / 0.2f);   // fade out over the last 0.2 s
            if (remaining <= 0f)
            {
                Hide();
                return;
            }
        }
        label.alpha = alpha;
    }
}
