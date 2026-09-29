using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// MUSIC ON / MUSIC OFF button. Holds no state of its own: it asks AudioManager to toggle and
/// redraws from AudioManager.MusicMutedChanged, so every copy in every scene shows the same state.
/// </summary>
[RequireComponent(typeof(Button))]
public class MusicToggleButton : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private string musicOnText = "MUSIC ON";
    [SerializeField] private string musicOffText = "MUSIC OFF";
    [Tooltip("Drop UI focus after clicking so the button doesn't stay highlighted during play.")]
    [SerializeField] private bool deselectAfterClick = true;

    private Button button;
    private ArcadeButton arcadeButton;

    private void Awake()
    {
        button = GetComponent<Button>();
        arcadeButton = GetComponent<ArcadeButton>();
        button.onClick.AddListener(OnClicked);
    }

    private void OnEnable()
    {
        AudioManager.MusicMutedChanged += OnMusicMutedChanged;
        Refresh();
    }

    private void Start()
    {
        // AudioManager may wake up after this button's OnEnable in the first scene.
        Refresh();
    }

    private void OnDisable()
    {
        AudioManager.MusicMutedChanged -= OnMusicMutedChanged;
    }

    private void OnClicked()
    {
        if (AudioManager.Instance == null) return;

        AudioManager.Instance.ToggleMusic();

        if (deselectAfterClick && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    private void OnMusicMutedChanged(bool muted)
    {
        Refresh();
    }

    private void Refresh()
    {
        AudioManager audio = AudioManager.Instance;
        bool muted = audio != null && audio.IsMusicMuted;

        if (label != null) label.text = muted ? musicOffText : musicOnText;
        if (arcadeButton != null) arcadeButton.SetStyle(muted ? ArcadeButton.Style.Secondary : ArcadeButton.Style.Primary);
        button.interactable = audio != null;
    }
}
