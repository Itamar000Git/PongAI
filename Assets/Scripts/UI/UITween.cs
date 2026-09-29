using System;
using UnityEngine;

/// <summary>
/// Tiny tween helper for UI polish (no third-party library): scale punch, fade/scale in and out,
/// short shake and a gentle looping pulse. Runs on unscaled time by default so it also works while
/// the match is frozen (pause / countdown). One component per animated element; no allocations.
/// </summary>
[DisallowMultipleComponent]
public class UITween : MonoBehaviour
{
    [SerializeField] private bool useUnscaledTime = true;

    private CanvasGroup group;
    private Vector3 baseScale = Vector3.one;
    private bool baseCaptured;

    // scale punch / scale-in
    private float scaleFrom = 1f, scaleElapsed, scaleDuration;
    private bool scaling;

    // fade
    private float alphaFrom, alphaTo, alphaElapsed, alphaDuration;
    private bool fading;
    private Action fadeDone;

    // shake (rotation wobble: layout groups ignore rotation, so it never fights UI layout)
    private float shakeAmplitude, shakeElapsed, shakeDuration;
    private bool shaking;

    // pulse (gentle, looping or a fixed number of cycles)
    private float pulseAmplitude, pulseHz, pulseElapsed, pulseEndsAt = -1f;
    private bool pulsing;

    private float DeltaTime => useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

    public CanvasGroup Group
    {
        get
        {
            if (group == null) group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
            return group;
        }
    }

    private void CaptureBase()
    {
        if (baseCaptured) return;
        baseCaptured = true;
        baseScale = transform.localScale;
    }

    /// <summary>Jumps to fromScale × base and eases back to the base scale.</summary>
    public void Punch(float fromScale, float duration)
    {
        CaptureBase();
        scaleFrom = fromScale;
        scaleElapsed = 0f;
        scaleDuration = Mathf.Max(0.01f, duration);
        scaling = true;
        Apply();
    }

    /// <summary>Fades the CanvasGroup from 0 to 1 while scaling in from fromScale.</summary>
    public void FadeIn(float duration, float fromScale = 1f)
    {
        Group.alpha = 0f;
        Fade(1f, duration, null);
        if (!Mathf.Approximately(fromScale, 1f)) Punch(fromScale, duration);
    }

    /// <summary>Fades the CanvasGroup to 0, then calls done (e.g. to deactivate).</summary>
    public void FadeOut(float duration, Action done)
    {
        Fade(0f, duration, done);
    }

    public void Fade(float to, float duration, Action done)
    {
        alphaFrom = Group.alpha;
        alphaTo = to;
        alphaElapsed = 0f;
        alphaDuration = Mathf.Max(0.01f, duration);
        fadeDone = done;
        fading = true;
    }

    public void StopFade()
    {
        fading = false;
        fadeDone = null;
    }

    /// <summary>Short rotational shake (degrees) that settles back to no rotation.</summary>
    public void Shake(float amplitude, float duration)
    {
        shakeAmplitude = amplitude;
        shakeElapsed = 0f;
        shakeDuration = Mathf.Max(0.01f, duration);
        shaking = true;
    }

    /// <summary>Gentle scale pulse. cycles &lt;= 0 loops until turned off.</summary>
    public void SetPulse(bool on, float amplitude = 0.04f, float hz = 0.8f, int cycles = 0)
    {
        CaptureBase();
        pulsing = on;
        pulseAmplitude = amplitude;
        pulseHz = hz;
        pulseElapsed = 0f;
        pulseEndsAt = on && cycles > 0 ? cycles / Mathf.Max(0.01f, hz) : -1f;
        if (!on) Apply();
    }

    private void Update()
    {
        float dt = DeltaTime;

        if (scaling)
        {
            scaleElapsed += dt;
            if (scaleElapsed >= scaleDuration) scaling = false;
        }

        if (pulsing)
        {
            pulseElapsed += dt;
            if (pulseEndsAt > 0f && pulseElapsed >= pulseEndsAt) pulsing = false;
        }

        if (fading)
        {
            alphaElapsed += dt;
            float t = Mathf.Clamp01(alphaElapsed / alphaDuration);
            Group.alpha = Mathf.Lerp(alphaFrom, alphaTo, 1f - (1f - t) * (1f - t));
            if (t >= 1f)
            {
                fading = false;
                Action done = fadeDone;
                fadeDone = null;
                done?.Invoke();
            }
        }

        if (shaking)
        {
            shakeElapsed += dt;
            float t = Mathf.Clamp01(shakeElapsed / shakeDuration);
            float angle = Mathf.Sin(shakeElapsed * 60f) * shakeAmplitude * (1f - t);
            transform.localRotation = Quaternion.Euler(0f, 0f, angle);
            if (t >= 1f)
            {
                shaking = false;
                transform.localRotation = Quaternion.identity;
            }
        }

        if (scaling || pulsing || baseCaptured) Apply();
    }

    private void Apply()
    {
        if (!baseCaptured) return;

        float scale = 1f;
        if (scaling)
        {
            float t = Mathf.Clamp01(scaleElapsed / scaleDuration);
            float eased = 1f - (1f - t) * (1f - t) * (1f - t);   // ease-out cubic
            scale = Mathf.Lerp(scaleFrom, 1f, eased);
        }
        if (pulsing)
            scale *= 1f + pulseAmplitude * (0.5f - 0.5f * Mathf.Cos(pulseElapsed * pulseHz * Mathf.PI * 2f));

        transform.localScale = baseScale * scale;
    }
}
