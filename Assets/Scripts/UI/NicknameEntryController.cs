using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Nickname screen: local validation first, then a debounced online lookup in BOTH leaderboards
/// (Classic and Learning, case-insensitive, whichever mode is being entered).
///   not found anywhere -> NICKNAME AVAILABLE, START works
///   found in either    -> NICKNAME ALREADY EXISTS + both bests + "IS THIS YOU?" YES / NO
///                         YES = accepted for this session (a lightweight returning-player confirmation; it does NOT
///                               prove ownership, there is no login), START works
///                         NO  = rejected, back to the input, START stays locked until another nickname is checked
///   lookup failed      -> COULDN'T CHECK NICKNAME + RETRY, START locked
/// START GAME only works for the exact nickname that was accepted; it then stores it in GameSession
/// (using the stored spelling for an existing player) and loads the game. The mode stays exactly as chosen;
/// Game Over later submits only to that mode's table.
/// </summary>
public class NicknameEntryController : MonoBehaviour
{
    private enum Availability { None, Checking, Available, Exists, Accepted, Rejected, Error }

    [SerializeField] private string gameSceneName = "Game";
    [SerializeField] private string mainMenuSceneName = "MainMenu";
    [SerializeField] private PongUITheme theme;
    [Tooltip("Wait this long after the last keystroke before asking Supabase (seconds).")]
    [SerializeField, Min(0f)] private float checkDebounceSeconds = 0.5f;

    [Header("UI")]
    [SerializeField] private TMP_InputField nicknameInput;
    [SerializeField] private Button startButton;
    [SerializeField] private Button backButton;
    [SerializeField] private TMP_Text validationText;
    [SerializeField] private TMP_Text counterText;
    [SerializeField] private Button retryCheckButton;
    [Tooltip("Small label naming the mode (the mode the game will be played in).")]
    [SerializeField] private TMP_Text modeText;

    [Header("Existing nickname (IS THIS YOU?)")]
    [Tooltip("Compact block shown for an existing nickname: both bests, and the question with YES / NO.")]
    [SerializeField] private GameObject existingPanel;
    [SerializeField] private TMP_Text scoresText;
    [Tooltip("The question and the YES / NO buttons (hidden once the nickname is accepted).")]
    [SerializeField] private GameObject confirmGroup;
    [SerializeField] private Button yesButton;
    [SerializeField] private Button noButton;

    private bool isLeaving;

    // Availability of exactly this (trimmed) nickname; anything else typed is unconfirmed.
    private Availability availability = Availability.None;
    private string checkedNickname;
    private NicknameLookupResult lookup;
    private string acceptedNickname;      // spelling stored on the leaderboard for an existing player
    private int checkSerial;              // bumps on every edit / retry: only the newest request may update the UI
    private string pendingNickname;
    private float pendingCheckAt = -1f;

    private static GameMode Mode => GameSession.CurrentModeOrDefault;

    private void Awake()
    {
        nicknameInput.lineType = TMP_InputField.LineType.SingleLine;
        nicknameInput.characterLimit = NicknameRules.MaxLength;
        nicknameInput.onValidateInput = (text, index, c) => NicknameRules.IsAllowedChar(c) ? c : '\0';
    }

    private void Start()
    {
        nicknameInput.onValueChanged.AddListener(_ => OnNicknameChanged());
        nicknameInput.onSubmit.AddListener(_ => StartGame());
        startButton.onClick.AddListener(StartGame);
        backButton.onClick.AddListener(Back);
        if (retryCheckButton != null) retryCheckButton.onClick.AddListener(RetryCheck);
        if (yesButton != null) yesButton.onClick.AddListener(ConfirmExisting);
        if (noButton != null) noButton.onClick.AddListener(RejectExisting);

        if (modeText != null)
            modeText.text = Mode == GameMode.Learning ? "LEARNING AI MODE" : "CLASSIC MODE";

        // Coming back from the game: keep the previous nickname (it is looked up again like any other).
        if (GameSession.Instance != null && !string.IsNullOrEmpty(GameSession.Instance.CurrentNickname))
            nicknameInput.SetTextWithoutNotify(GameSession.Instance.CurrentNickname);

        OnNicknameChanged();

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(nicknameInput.gameObject);
        nicknameInput.ActivateInputField();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) Back();

        if (pendingCheckAt >= 0f && Time.unscaledTime >= pendingCheckAt)
        {
            pendingCheckAt = -1f;
            CheckNicknameAsync(pendingNickname, checkSerial);
        }
    }

    private void OnNicknameChanged()
    {
        // Any edit invalidates the previous result and any request still in flight.
        checkSerial++;
        pendingCheckAt = -1f;
        lookup = null;
        acceptedNickname = null;

        string nickname = NicknameRules.Clean(nicknameInput.text);
        counterText.text = $"{nickname.Length}/{NicknameRules.MaxLength}";

        NicknameRules.Result local = NicknameRules.Validate(nickname);
        if (local != NicknameRules.Result.Valid)
        {
            SetAvailability(Availability.None, null);
            validationText.text = NicknameRules.Message(local);
            if (theme != null)
                validationText.color = local == NicknameRules.Result.Empty ? theme.disabledText : theme.secondary;
            return;
        }

        // Locally valid: debounce, then look the nickname up in both leaderboards.
        SetAvailability(Availability.Checking, null);
        pendingNickname = nickname;
        pendingCheckAt = Time.unscaledTime + checkDebounceSeconds;
    }

    private async void CheckNicknameAsync(string nickname, int serial)
    {
        NicknameLookupResult result = await NicknameLookup.LookupAsync(nickname);

        // Ignore stale answers: the screen was left, or the input changed / a retry started while the request was running.
        if (this == null || serial != checkSerial || NicknameRules.Clean(nicknameInput.text) != nickname)
            return;

        if (!result.Success)
        {
            SetAvailability(Availability.Error, null);
            return;
        }

        lookup = result;
        if (!result.ExistsAnywhere)
        {
            SetAvailability(Availability.Available, nickname);
            return;
        }

        // Already accepted earlier in this session (e.g. back from a match): no need to ask again.
        if (GameSession.Instance != null && GameSession.Instance.IsSessionNickname(nickname))
        {
            AcceptExisting(nickname);
            return;
        }

        SetAvailability(Availability.Exists, nickname);
    }

    private void RetryCheck()
    {
        string nickname = NicknameRules.Clean(nicknameInput.text);
        if (NicknameRules.Validate(nickname) != NicknameRules.Result.Valid) return;

        checkSerial++;
        SetAvailability(Availability.Checking, null);
        pendingNickname = nickname;
        pendingCheckAt = Time.unscaledTime;   // no debounce for an explicit retry
    }

    // ---------------------------------------------------------------- IS THIS YOU?

    /// <summary>YES: accept this existing nickname for this session and use its stored spelling.</summary>
    private void ConfirmExisting()
    {
        if (availability != Availability.Exists || lookup == null || isLeaving) return;
        if (NicknameRules.Clean(nicknameInput.text) != checkedNickname) return;

        AcceptExisting(checkedNickname);
    }

    private void AcceptExisting(string typedNickname)
    {
        string stored = lookup != null ? lookup.CanonicalNickname(Mode) : typedNickname;
        if (string.IsNullOrEmpty(stored)) stored = typedNickname;

        acceptedNickname = stored;
        if (stored != typedNickname) nicknameInput.SetTextWithoutNotify(stored);   // show the stored spelling
        SetAvailability(Availability.Accepted, stored);
    }

    /// <summary>NO: this is not the same player. Back to the input; START stays locked until another nickname is checked.</summary>
    private void RejectExisting()
    {
        if (availability != Availability.Exists || isLeaving) return;

        SetAvailability(Availability.Rejected, null);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(nicknameInput.gameObject);
        nicknameInput.ActivateInputField();
        nicknameInput.selectionAnchorPosition = 0;
        nicknameInput.selectionFocusPosition = nicknameInput.text.Length;
    }

    private void SetAvailability(Availability value, string nickname)
    {
        availability = value;
        bool known = value == Availability.Available || value == Availability.Exists || value == Availability.Accepted;
        checkedNickname = known ? nickname : null;
        startButton.interactable = CanStart();

        bool showRetry = value == Availability.Error;
        if (retryCheckButton != null) retryCheckButton.gameObject.SetActive(showRetry);
        counterText.gameObject.SetActive(!showRetry);

        bool showExisting = (value == Availability.Exists || value == Availability.Accepted) && lookup != null;
        if (existingPanel != null) existingPanel.SetActive(showExisting);
        if (confirmGroup != null) confirmGroup.SetActive(value == Availability.Exists);
        if (showExisting && scoresText != null) scoresText.text = ScoresLine(lookup);

        switch (value)
        {
            case Availability.Checking:
                SetStatus("CHECKING...", theme != null ? theme.disabledText : Color.gray);
                break;
            case Availability.Available:
                SetStatus("NICKNAME AVAILABLE", theme != null ? theme.primary : Color.cyan);
                break;
            case Availability.Exists:
                SetStatus("NICKNAME ALREADY EXISTS", theme != null ? theme.secondary : Color.red);
                break;
            case Availability.Accepted:
                SetStatus("WELCOME BACK", theme != null ? theme.primary : Color.cyan);
                break;
            case Availability.Rejected:
                SetStatus("CHOOSE ANOTHER NICKNAME", theme != null ? theme.secondary : Color.red);
                break;
            case Availability.Error:
                SetStatus("COULDN'T CHECK NICKNAME", theme != null ? theme.secondary : Color.red);
                break;
        }
    }

    private static string ScoresLine(NicknameLookupResult result)
    {
        string classic = result.ExistsInClassic ? result.ClassicBest.ToString() : "—";
        string learning = result.ExistsInLearning ? result.LearningBest.ToString() : "—";
        return $"CLASSIC BEST: {classic}\nLEARNING BEST: {learning}";
    }

    private void SetStatus(string text, Color color)
    {
        validationText.text = text;
        validationText.color = color;
    }

    /// <summary>Only the exact, locally valid nickname that was accepted (available, or confirmed YES) may start a game.</summary>
    private bool CanStart()
    {
        string nickname = NicknameRules.Clean(nicknameInput.text);
        return !isLeaving
               && (availability == Availability.Available || availability == Availability.Accepted)
               && NicknameRules.Validate(nickname) == NicknameRules.Result.Valid
               && nickname == checkedNickname;
    }

    private void StartGame()
    {
        // Button and Enter (onSubmit) both land here, so neither can bypass the check or the confirmation.
        if (!CanStart()) return;

        isLeaving = true;
        GameSession.GetOrCreate().SetNickname(acceptedNickname ?? nicknameInput.text);
        SceneTransition.LoadScene(gameSceneName);
    }

    private void Back()
    {
        if (isLeaving) return;

        isLeaving = true;
        SceneTransition.LoadScene(mainMenuSceneName);
    }
}
