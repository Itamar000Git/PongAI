using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// MUSIC / SFX settings: an ON/OFF toggle and a volume slider each. Holds no state of its own;
/// it reads and writes the persistent AudioManager, so values survive scene changes.
/// </summary>
public class AudioSettingsPanel : MonoBehaviour
{
    [Header("Music")]
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Button musicToggle;
    [SerializeField] private TMP_Text musicToggleLabel;
    [SerializeField] private TMP_Text musicValueText;

    [Header("SFX")]
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private Button sfxToggle;
    [SerializeField] private TMP_Text sfxToggleLabel;
    [SerializeField] private TMP_Text sfxValueText;
    [Tooltip("Plays a short sample while dragging the SFX slider, at most this often (seconds, unscaled).")]
    [SerializeField, Min(0f)] private float sfxPreviewInterval = 0.2f;

    private float lastSfxPreviewTime = float.NegativeInfinity;

    private void Awake()
    {
        musicSlider.onValueChanged.AddListener(OnMusicVolumeChanged);
        sfxSlider.onValueChanged.AddListener(OnSfxVolumeChanged);
        musicToggle.onClick.AddListener(ToggleMusic);
        sfxToggle.onClick.AddListener(ToggleSfx);
    }

    private void OnEnable()
    {
        AudioManager.MusicMutedChanged += OnMutedChanged;
        AudioManager.SfxMutedChanged += OnMutedChanged;
        Refresh();
    }

    private void OnDisable()
    {
        AudioManager.MusicMutedChanged -= OnMutedChanged;
        AudioManager.SfxMutedChanged -= OnMutedChanged;
    }

    private void OnMutedChanged(bool muted)
    {
        Refresh();
    }

    private void Refresh()
    {
        AudioManager audio = AudioManager.Instance;
        bool available = audio != null;

        musicSlider.interactable = sfxSlider.interactable = available;
        musicToggle.interactable = sfxToggle.interactable = available;
        if (!available) return;

        musicSlider.SetValueWithoutNotify(audio.MusicVolume);
        sfxSlider.SetValueWithoutNotify(audio.SfxVolume);
        SetToggle(musicToggle, musicToggleLabel, !audio.IsMusicMuted);
        SetToggle(sfxToggle, sfxToggleLabel, !audio.IsSfxMuted);
        SetPercent(musicValueText, audio.MusicVolume);
        SetPercent(sfxValueText, audio.SfxVolume);
    }

    private void OnMusicVolumeChanged(float value)
    {
        if (AudioManager.Instance == null) return;

        AudioManager.Instance.SetMusicVolume(value);
        SetPercent(musicValueText, value);
    }

    private void OnSfxVolumeChanged(float value)
    {
        AudioManager audio = AudioManager.Instance;
        if (audio == null) return;

        audio.SetSfxVolume(value);
        SetPercent(sfxValueText, value);

        // Let the player hear the new level (works while the game is paused: unscaled time).
        if (!audio.IsSfxMuted && Time.unscaledTime - lastSfxPreviewTime >= sfxPreviewInterval)
        {
            lastSfxPreviewTime = Time.unscaledTime;
            audio.PlayBallHit();
        }
    }

    private void ToggleMusic()
    {
        if (AudioManager.Instance != null) AudioManager.Instance.ToggleMusic();
    }

    private void ToggleSfx()
    {
        AudioManager audio = AudioManager.Instance;
        if (audio != null) audio.SetSfxMuted(!audio.IsSfxMuted);
    }

    private static void SetToggle(Button button, TMP_Text label, bool on)
    {
        if (label != null) label.text = on ? "ON" : "OFF";
        ArcadeButton arcade = button as ArcadeButton;
        if (arcade != null) arcade.SetStyle(on ? ArcadeButton.Style.Primary : ArcadeButton.Style.Secondary);
    }

    private static void SetPercent(TMP_Text text, float value)
    {
        if (text != null) text.text = Mathf.RoundToInt(value * 100f) + "%";
    }
}
