using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Fade-to-navy scene changes. Created on first use and kept across scenes (DontDestroyOnLoad).
/// Fade out -> optional beforeLoad callback (e.g. GameManager.PrepareForSceneExit) -> load -> fade in.
/// Runs on unscaled time, blocks clicks while covering the screen, and ignores new requests mid-transition.
/// </summary>
public class SceneTransition : MonoBehaviour
{
    private const float FadeOutSeconds = 0.3f;
    private const float FadeInSeconds = 0.3f;
    private static readonly Color FadeColor = new Color32(6, 11, 30, 255);   // theme backgroundDeep

    private static SceneTransition instance;

    private Image overlay;
    private bool busy;

    public static bool IsTransitioning => instance != null && instance.busy;

    /// <summary>Fades out, runs beforeLoad (if any), loads the scene by name, then fades back in.</summary>
    public static void LoadScene(string sceneName, Action beforeLoad = null)
    {
        EnsureInstance().Begin(sceneName, beforeLoad);
    }

    private static SceneTransition EnsureInstance()
    {
        if (instance != null) return instance;

        var go = new GameObject(nameof(SceneTransition));
        DontDestroyOnLoad(go);
        instance = go.AddComponent<SceneTransition>();
        instance.BuildOverlay();
        return instance;
    }

    private void BuildOverlay()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;   // above every game canvas
        gameObject.AddComponent<GraphicRaycaster>();

        var imageGo = new GameObject("Fade", typeof(RectTransform));
        imageGo.transform.SetParent(transform, false);
        var rt = (RectTransform)imageGo.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;

        overlay = imageGo.AddComponent<Image>();
        overlay.color = new Color(FadeColor.r, FadeColor.g, FadeColor.b, 0f);
        overlay.raycastTarget = false;
    }

    private void Begin(string sceneName, Action beforeLoad)
    {
        if (busy) return;
        StartCoroutine(Run(sceneName, beforeLoad));
    }

    private IEnumerator Run(string sceneName, Action beforeLoad)
    {
        busy = true;
        overlay.raycastTarget = true;   // no more clicks on the old scene

        yield return FadeTo(1f, FadeOutSeconds);

        beforeLoad?.Invoke();
        AsyncOperation load = SceneManager.LoadSceneAsync(sceneName);
        while (load != null && !load.isDone) yield return null;
        yield return null;   // let the new scene run its first frame under the cover

        overlay.raycastTarget = false;
        busy = false;
        yield return FadeTo(0f, FadeInSeconds);
    }

    private IEnumerator FadeTo(float target, float seconds)
    {
        float start = overlay.color.a;
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            // Clamp the step so a long loading frame doesn't skip the fade.
            elapsed += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            SetAlpha(Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, elapsed / seconds)));
            yield return null;
        }
        SetAlpha(target);
    }

    private void SetAlpha(float a)
    {
        Color c = overlay.color;
        c.a = a;
        overlay.color = c;
    }
}
