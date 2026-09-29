using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One RANK / PLAYER / SCORE line of the leaderboard table (styling only; ranking comes from the service).</summary>
public class LeaderboardRowView : MonoBehaviour
{
    [SerializeField] private PongUITheme theme;
    [SerializeField] private Graphic background;
    [SerializeField] private Graphic accent;
    [SerializeField] private TMP_Text rankText;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text scoreText;

    [Header("Sizes")]
    [SerializeField] private float rankFontSize = 34f;
    [SerializeField] private float topRankFontSize = 42f;

    private static readonly Color EvenRow = new Color32(19, 33, 70, 235);   // lighter navy
    private static readonly Color OddRow = new Color32(8, 15, 38, 235);     // deep navy

    public void Show(int rank, LeaderboardEntry entry, bool isCurrentPlayer)
    {
        rankText.text = rank.ToString();
        nameText.text = entry.nickname;
        scoreText.text = entry.high_score.ToString();

        bool top3 = rank <= 3;
        rankText.fontSize = top3 ? topRankFontSize : rankFontSize;

        if (theme != null)
        {
            // Top 3 get their own accent color: 1st yellow, 2nd cyan, 3rd orange.
            Color podium = rank == 1 ? theme.highlight : rank == 2 ? theme.primary : theme.secondary;
            Color rowColor = rank % 2 == 1 ? OddRow : EvenRow;
            if (top3) rowColor = Color.Lerp(rowColor, podium, 0.14f);

            background.color = isCurrentPlayer ? theme.secondary : rowColor;

            if (accent != null)
            {
                accent.gameObject.SetActive(top3 || isCurrentPlayer);
                accent.color = isCurrentPlayer ? theme.highlight : podium;
            }

            // Current player: solid orange row with white text for contrast (the yellow accent bar marks it too).
            rankText.color = isCurrentPlayer ? theme.text : top3 ? podium : theme.primary;
            nameText.color = theme.text;
            scoreText.color = isCurrentPlayer ? theme.text : theme.highlight;
        }

        gameObject.SetActive(true);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }
}
