using TMPro;
using UnityEngine;

/// <summary>
/// Applies the theme font and the material preset for the text's role,
/// so replacing the font in PongUITheme updates every text in the game.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(TMP_Text))]
public class ThemedText : MonoBehaviour
{
    public enum Role { Title, Button, Body }

    [SerializeField] private PongUITheme theme;
    [SerializeField] private Role role = Role.Body;

    private void OnEnable()
    {
        Apply();
    }

    [ContextMenu("Apply Theme")]
    public void Apply()
    {
        if (theme == null || theme.font == null) return;

        TMP_Text text = GetComponent<TMP_Text>();
        text.font = theme.font;

        Material preset = role switch
        {
            Role.Title => theme.titleMaterial,
            Role.Button => theme.buttonMaterial,
            _ => theme.bodyMaterial
        };

        // A preset only works with the atlas it was made for; fall back to the font's default material.
        bool presetMatchesFont = preset != null && preset.mainTexture == theme.font.atlasTexture;
        text.fontSharedMaterial = presetMatchesFont ? preset : theme.font.material;
    }
}
