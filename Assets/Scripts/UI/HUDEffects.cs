using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD reactions to match events (visual only): positive / negative screen-edge flash, SCORE and
/// LIVES punches, "+1" / "-1 LIFE" popups, a soft low-lives pulse and a small flash on GO!.
/// Listens to GameManager events; never changes match state.
/// </summary>
public class HUDEffects : MonoBehaviour
{
    [SerializeField] private GameManager gameManager;
    [SerializeField] private PongUITheme theme;

    [Header("Edge flash")]
    [SerializeField] private UITween edgeFlash;
    [SerializeField] private Image[] edgeBars;

    [Header("Pills")]
    [SerializeField] private UITween scorePill;
    [SerializeField] private UITween livesPill;
    [SerializeField] private Outline livesOutline;
    [SerializeField] private int lowLivesThreshold = 3;

    [Header("Popups")]
    [SerializeField] private TMP_Text scorePopup;
    [SerializeField] private TMP_Text livesPopup;
    [SerializeField] private float popupSeconds = 0.7f;
    [SerializeField] private float popupDrift = 36f;

    private struct Popup
    {
        public TMP_Text text;
        public Vector2 start;
        public float elapsed;
        public bool active;
    }

    private Popup score;
    private Popup lives;
    private Color livesOutlineNormal;
    private bool lowLives;
    private MatchState lastState;

    private void Awake()
    {
        score = new Popup { text = scorePopup, start = scorePopup.rectTransform.anchoredPosition };
        lives = new Popup { text = livesPopup, start = livesPopup.rectTransform.anchoredPosition };
        scorePopup.gameObject.SetActive(false);
        livesPopup.gameObject.SetActive(false);
        if (livesOutline != null) livesOutlineNormal = livesOutline.effectColor;
        edgeFlash.Group.alpha = 0f;
        edgeFlash.Group.blocksRaycasts = false;
    }

    private void OnEnable()
    {
        gameManager.GoalCounted += OnGoalCounted;
        gameManager.StateChanged += OnStateChanged;
        lastState = gameManager.State;
    }

    private void OnDisable()
    {
        gameManager.GoalCounted -= OnGoalCounted;
        gameManager.StateChanged -= OnStateChanged;
    }

    private void OnGoalCounted(GoalSide side, bool lifeLost)
    {
        if (side == GoalSide.Right)
        {
            Flash(theme != null ? theme.primary : Color.cyan, 0.45f, 0.45f);
            scorePill.Punch(1.3f, 0.3f);
            Show(ref score, "+1");
        }
        else if (lifeLost)
        {
            Flash(theme != null ? theme.secondary : new Color(1f, 0.4f, 0.1f), 0.5f, 0.5f);
            livesPill.Punch(1.25f, 0.3f);
            livesPill.Shake(7f, 0.3f);
            Show(ref lives, "-1 LIFE");
        }

        RefreshLowLives();
    }

    private void OnStateChanged(MatchState state)
    {
        // Small court flash when the countdown hands over to play (GO!).
        if (lastState == MatchState.Countdown && state == MatchState.Playing)
            Flash(theme != null ? theme.highlight : Color.yellow, 0.25f, 0.35f);

        lastState = state;
        RefreshLowLives();
    }

    private void RefreshLowLives()
    {
        bool low = gameManager.State != MatchState.GameOver && gameManager.PlayerLives <= lowLivesThreshold;
        if (low == lowLives) return;

        lowLives = low;
        livesPill.SetPulse(low, 0.05f, 0.9f);
        if (livesOutline != null)
            livesOutline.effectColor = low && theme != null ? theme.secondary : livesOutlineNormal;
    }

    private void Flash(Color color, float peakAlpha, float seconds)
    {
        foreach (Image bar in edgeBars) bar.color = color;
        edgeFlash.StopFade();
        edgeFlash.Group.alpha = peakAlpha;
        edgeFlash.FadeOut(seconds, null);
    }

    private static void Show(ref Popup popup, string message)
    {
        popup.text.text = message;
        popup.text.rectTransform.anchoredPosition = popup.start;
        popup.elapsed = 0f;
        popup.active = true;
        popup.text.gameObject.SetActive(true);
    }

    private void Update()
    {
        Animate(ref score);
        Animate(ref lives);
    }

    private void Animate(ref Popup popup)
    {
        if (!popup.active) return;

        popup.elapsed += Time.unscaledDeltaTime;
        float t = Mathf.Clamp01(popup.elapsed / popupSeconds);
        popup.text.rectTransform.anchoredPosition = popup.start + Vector2.down * (popupDrift * (1f - (1f - t) * (1f - t)));
        popup.text.alpha = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
        popup.text.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.3f, 1f, Mathf.Clamp01(t * 4f));
        if (t >= 1f)
        {
            popup.active = false;
            popup.text.gameObject.SetActive(false);
        }
    }
}
