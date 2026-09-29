using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

/// <summary>
/// Game-wide audio. One persistent instance (DontDestroyOnLoad) owns the music and SFX sources,
/// so music keeps playing across scene loads. Music/SFX volume and mute are saved in PlayerPrefs
/// and restored on the next launch (AudioManager is the only owner of these settings).
/// An AudioManager prefab instance can sit in every scene; extra copies destroy themselves.
/// Each scene maps to a track (menu or game); changing tracks fades out, swaps and fades in.
/// </summary>
[DisallowMultipleComponent]
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    /// <summary>Raised with the new muted state whenever music is muted or unmuted.</summary>
    public static event Action<bool> MusicMutedChanged;

    /// <summary>Raised whenever a sound effect is played (for debug views, subtitles, etc.).</summary>
    public static event Action<AudioClip> SfxPlayed;

    /// <summary>Raised with the new muted state whenever SFX are muted or unmuted.</summary>
    public static event Action<bool> SfxMutedChanged;

    [Header("Music")]
    [SerializeField] private AudioSource musicSource;
    [Tooltip("Master music volume. Each track's own volume below is multiplied by this.")]
    [SerializeField, Range(0f, 1f)] private float musicVolume = 1f;
    [SerializeField] private bool musicMuted;

    [Header("Tracks")]
    [FormerlySerializedAs("musicClip")]
    [SerializeField] private AudioClip menuMusic;
    [SerializeField, Range(0f, 1f)] private float menuMusicVolume = 0.6f;
    [SerializeField] private AudioClip gameMusic;
    [Tooltip("Quieter so it doesn't distract from play.")]
    [SerializeField, Range(0f, 1f)] private float gameMusicVolume = 0.4f;
    [Tooltip("Scenes that use gameMusic. Every other scene uses menuMusic.")]
    [SerializeField] private string[] gameMusicScenes = { "Game" };
    [Tooltip("Total time of a track change: half fading out, half fading in.")]
    [SerializeField, Min(0f)] private float musicFadeDuration = 0.8f;

    [Header("SFX")]
    [SerializeField] private AudioSource sfxSource;
    [Tooltip("Master SFX volume. Each effect's own volume below is multiplied by this. Independent of music mute.")]
    [SerializeField, Range(0f, 1f)] private float sfxVolume = 1f;
    [Tooltip("Reserved for a future SFX on/off setting. The MUSIC button does not affect SFX.")]
    [SerializeField] private bool sfxMuted;

    [Header("Gameplay SFX")]
    [SerializeField] private AudioClip ballHitSfx;
    [SerializeField, Range(0f, 1f)] private float ballHitVolume = 0.5f;
    [SerializeField] private AudioClip wallHitSfx;
    [SerializeField, Range(0f, 1f)] private float wallHitVolume = 0.4f;
    [SerializeField] private AudioClip positiveScoreSfx;
    [SerializeField, Range(0f, 1f)] private float positiveScoreVolume = 0.75f;
    [SerializeField] private AudioClip lifeLostSfx;
    [SerializeField, Range(0f, 1f)] private float lifeLostVolume = 0.65f;
    [SerializeField] private AudioClip gameOverSfx;
    [SerializeField, Range(0f, 1f)] private float gameOverVolume = 0.85f;
    [Tooltip("Game Over always follows the last life lost; this lets the life-lost buzz start before the sting.")]
    [SerializeField, Min(0f)] private float gameOverSfxDelay = 0.4f;

    // PlayerPrefs keys for the persisted audio settings.
    public const string MusicVolumeKey = "PongAI.Audio.MusicVolume";
    public const string SfxVolumeKey = "PongAI.Audio.SfxVolume";
    public const string MusicMutedKey = "PongAI.Audio.MusicMuted";
    public const string SfxMutedKey = "PongAI.Audio.SfxMuted";
    private const float SaveDelaySeconds = 0.5f;   // slider drags: write to disk once dragging settles

    private float defaultMusicVolume;
    private float defaultSfxVolume;
    private bool settingsDirty;
    private float saveAt = -1f;

    private AudioClip targetClip;   // track that is playing or being faded to
    private float fadeLevel;        // 0..1, applied on top of the configured volumes
    private Coroutine transition;

    public bool IsMusicMuted => musicMuted;
    public bool HasMusicClip => musicSource != null && musicSource.clip != null;
    public AudioClip CurrentMusic => musicSource != null ? musicSource.clip : null;
    public AudioClip TargetMusic => targetClip;
    public bool IsMusicTransitioning => transition != null;
    public bool IsSfxMuted => sfxMuted;

    public float SfxVolume
    {
        get => sfxVolume;
        set
        {
            float clamped = Mathf.Clamp01(value);
            bool changed = !Mathf.Approximately(sfxVolume, clamped);
            sfxVolume = clamped;
            ApplySfxSettings();
            if (changed)
            {
                PlayerPrefs.SetFloat(SfxVolumeKey, sfxVolume);
                ScheduleSave();
            }
        }
    }

    public float MusicVolume
    {
        get => musicVolume;
        set
        {
            float clamped = Mathf.Clamp01(value);
            bool changed = !Mathf.Approximately(musicVolume, clamped);
            musicVolume = clamped;
            ApplyMusicSettings();
            if (changed)
            {
                PlayerPrefs.SetFloat(MusicVolumeKey, musicVolume);
                ScheduleSave();
            }
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Serialized values are the defaults; saved settings (if any) override them.
        defaultMusicVolume = musicVolume;
        defaultSfxVolume = sfxVolume;
        LoadSettings();

        if (musicSource != null)
        {
            musicSource.loop = true;
            musicSource.playOnAwake = false;
            musicSource.spatialBlend = 0f;
        }
        if (sfxSource != null)
        {
            sfxSource.loop = false;
            sfxSource.playOnAwake = false;
            sfxSource.spatialBlend = 0f;
        }
        ApplySfxSettings();

        SceneManager.sceneLoaded += OnSceneLoaded;

        // Whatever scene the game starts in, fade in the track that belongs to it.
        ApplyMusicSettings();
        PlayMusic(MusicForScene(SceneManager.GetActiveScene().name));
    }

    private void Update()
    {
        if (saveAt >= 0f && Time.unscaledTime >= saveAt)
            SaveSettings();
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused) SaveSettings();
    }

    private void OnApplicationQuit()
    {
        SaveSettings();
    }

    private void OnDestroy()
    {
        if (Instance != this) return;

        SaveSettings();
        Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single)
            PlayMusic(MusicForScene(scene.name));
    }

    public AudioClip MusicForScene(string sceneName)
    {
        return Array.IndexOf(gameMusicScenes, sceneName) >= 0 ? gameMusic : menuMusic;
    }

    public void ToggleMusic()
    {
        SetMusicMuted(!musicMuted);
    }

    public void SetMusicMuted(bool muted)
    {
        musicMuted = muted;
        ApplyMusicSettings();
        PlayerPrefs.SetInt(MusicMutedKey, musicMuted ? 1 : 0);
        SaveSettings(force: true);
        MusicMutedChanged?.Invoke(musicMuted);
    }

    /// <summary>
    /// Fades to a looping track. Asking for the track that is already playing (or already being
    /// faded to) does nothing, so it keeps its playback position. Works while muted too.
    /// </summary>
    public void PlayMusic(AudioClip clip)
    {
        if (musicSource == null || clip == null || clip == targetClip) return;

        targetClip = clip;
        if (transition != null) StopCoroutine(transition);
        transition = StartCoroutine(SwitchMusic(clip));
    }

    public void PlayBallHit() => PlaySfx(ballHitSfx, ballHitVolume);
    public void PlayWallHit() => PlaySfx(wallHitSfx, wallHitVolume);
    public void PlayPositiveScore() => PlaySfx(positiveScoreSfx, positiveScoreVolume);
    public void PlayLifeLost() => PlaySfx(lifeLostSfx, lifeLostVolume);

    public void PlayGameOver()
    {
        if (gameOverSfxDelay > 0f)
            StartCoroutine(PlaySfxAfterDelay(gameOverSfx, gameOverVolume, gameOverSfxDelay));
        else
            PlaySfx(gameOverSfx, gameOverVolume);
    }

    /// <summary>Master music volume (0-1). Kept while muted and applied on unmute.</summary>
    public void SetMusicVolume(float value)
    {
        MusicVolume = value;
    }

    /// <summary>Master SFX volume (0-1). Kept while muted and applied on unmute.</summary>
    public void SetSfxVolume(float value)
    {
        SfxVolume = value;
    }

    public void SetSfxMuted(bool muted)
    {
        sfxMuted = muted;
        ApplySfxSettings();
        PlayerPrefs.SetInt(SfxMutedKey, sfxMuted ? 1 : 0);
        SaveSettings(force: true);
        SfxMutedChanged?.Invoke(sfxMuted);
    }

    /// <summary>Default volumes, music and SFX on; saved immediately.</summary>
    public void ResetAudioSettingsToDefaults()
    {
        MusicVolume = defaultMusicVolume;
        SfxVolume = defaultSfxVolume;
        SetSfxMuted(false);
        SetMusicMuted(false);   // raises MusicMutedChanged last so every settings UI refreshes
        SaveSettings(force: true);
    }

    // ---------------------------------------------------------------- persistence

    /// <summary>Reads saved settings once at startup; missing keys keep the serialized defaults.</summary>
    private void LoadSettings()
    {
        if (PlayerPrefs.HasKey(MusicVolumeKey)) musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicVolumeKey));
        if (PlayerPrefs.HasKey(SfxVolumeKey)) sfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxVolumeKey));
        if (PlayerPrefs.HasKey(MusicMutedKey)) musicMuted = PlayerPrefs.GetInt(MusicMutedKey) != 0;
        if (PlayerPrefs.HasKey(SfxMutedKey)) sfxMuted = PlayerPrefs.GetInt(SfxMutedKey) != 0;
    }

    private void ScheduleSave()
    {
        settingsDirty = true;
        saveAt = Time.unscaledTime + SaveDelaySeconds;
    }

    /// <summary>Writes pending settings to disk (immediately when forced, e.g. mute toggles).</summary>
    private void SaveSettings(bool force = false)
    {
        if (!force && !settingsDirty) return;

        settingsDirty = false;
        saveAt = -1f;
        PlayerPrefs.Save();
    }

    /// <summary>One-shot sound effect on the SFX source; never touches the music source.</summary>
    public void PlaySfx(AudioClip clip, float volumeScale = 1f)
    {
        if (sfxSource == null || clip == null || sfxMuted) return;

        // Master SFX volume lives on the source (see ApplySfxSettings), so slider changes apply live.
        sfxSource.PlayOneShot(clip, volumeScale);
        SfxPlayed?.Invoke(clip);
    }

    private IEnumerator PlaySfxAfterDelay(AudioClip clip, float volumeScale, float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        PlaySfx(clip, volumeScale);
    }

    private IEnumerator SwitchMusic(AudioClip clip)
    {
        float halfFade = musicFadeDuration * 0.5f;

        // Fade out whatever else is playing. If we were fading away from this same clip, skip
        // straight to fading it back in so it continues where it is.
        if (musicSource.clip != null && musicSource.clip != clip)
            yield return FadeTo(0f, halfFade);

        if (musicSource.clip != clip)
        {
            musicSource.clip = clip;
            musicSource.Play();
        }

        yield return FadeTo(1f, halfFade);
        transition = null;
    }

    private IEnumerator FadeTo(float target, float duration)
    {
        while (!Mathf.Approximately(fadeLevel, target))
        {
            fadeLevel = duration > 0f
                ? Mathf.MoveTowards(fadeLevel, target, Time.unscaledDeltaTime / duration)
                : target;
            ApplyMusicSettings();
            yield return null;
        }
    }

    private void ApplySfxSettings()
    {
        if (sfxSource == null) return;

        sfxSource.volume = sfxVolume;
        sfxSource.mute = sfxMuted;
    }

    private float TrackVolume(AudioClip clip)
    {
        if (clip != null && clip == gameMusic) return gameMusicVolume;
        if (clip != null && clip == menuMusic) return menuMusicVolume;
        return 1f;
    }

    private void ApplyMusicSettings()
    {
        if (musicSource == null) return;

        // Mute overrides every volume; the track keeps playing silently so unmuting resumes in place.
        musicSource.volume = musicVolume * TrackVolume(musicSource.clip) * fadeLevel;
        musicSource.mute = musicMuted;
    }
}
