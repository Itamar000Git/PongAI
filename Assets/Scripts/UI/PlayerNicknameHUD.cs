using TMPro;
using UnityEngine;

/// <summary>
/// Shows the session nickname in the game HUD ("PLAYER" when the Game scene is started directly).
/// </summary>
public class PlayerNicknameHUD : MonoBehaviour
{
    [SerializeField] private TMP_Text nicknameText;

    private void Start()
    {
        if (nicknameText != null) nicknameText.text = GameSession.NicknameOrDefault;
    }
}
