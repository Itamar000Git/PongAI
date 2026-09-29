using TMPro;
using UnityEngine;

/// <summary>
/// Shared visual style for every PongAI menu/HUD screen.
/// Swap the font or tweak the palette here and every themed element follows.
/// </summary>
[CreateAssetMenu(fileName = "PongUITheme", menuName = "PongAI/UI Theme")]
public class PongUITheme : ScriptableObject
{
    [Header("Palette")]
    public Color background = new Color32(11, 20, 48, 255);      // dark navy
    public Color backgroundDeep = new Color32(6, 11, 30, 255);   // darker navy (panels, shadows)
    public Color primary = new Color32(31, 214, 209, 255);       // turquoise / cyan
    public Color primaryDark = new Color32(18, 150, 160, 255);
    public Color secondary = new Color32(255, 138, 31, 255);     // orange
    public Color secondaryDark = new Color32(214, 96, 14, 255);
    public Color highlight = new Color32(255, 210, 63, 255);     // warm yellow
    public Color text = Color.white;
    public Color outline = new Color32(8, 12, 30, 255);          // near-black navy outline/shadow
    public Color disabled = new Color32(88, 97, 114, 255);       // gray
    public Color disabledText = new Color32(168, 175, 188, 255);

    [Header("Typography")]
    [Tooltip("Main UI font. Replace with an arcade font later; material presets must match its atlas.")]
    public TMP_FontAsset font;
    [Tooltip("Huge titles: thick outline + drop shadow.")]
    public Material titleMaterial;
    [Tooltip("Button labels: medium outline + small drop shadow.")]
    public Material buttonMaterial;
    [Tooltip("Body/small text: thin outline.")]
    public Material bodyMaterial;
}
