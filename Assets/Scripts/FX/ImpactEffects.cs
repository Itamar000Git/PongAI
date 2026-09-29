using UnityEngine;

/// <summary>
/// Short visual feedback for ball collisions: a paddle color flash plus a small burst at the contact
/// point, and a subtler flattened pulse on wall hits. Uses a small pool of sprites created once;
/// visual only (never changes transforms of paddles, colliders or physics). Runs on scaled time so
/// effects freeze with the game while paused.
/// </summary>
public class ImpactEffects : MonoBehaviour
{
    [SerializeField] private PongUITheme theme;
    [SerializeField] private Collider2D[] paddles;
    [SerializeField] private Collider2D[] walls;
    [SerializeField] private Sprite burstSprite;

    [Header("Paddle")]
    [SerializeField] private float paddleFlashSeconds = 0.15f;
    [SerializeField] private float paddleBurstSeconds = 0.18f;
    [SerializeField] private float paddleBurstSize = 0.9f;

    [Header("Wall")]
    [SerializeField] private float wallPulseSeconds = 0.15f;
    [SerializeField] private Vector2 wallPulseSize = new Vector2(1.6f, 0.3f);

    [Tooltip("Ignores a re-contact with the same collider within this time, so one hit = one effect.")]
    [SerializeField] private float sameColliderCooldown = 0.08f;

    private const int PoolSize = 6;

    private struct Burst
    {
        public SpriteRenderer renderer;
        public float elapsed, duration;
        public Vector2 fromScale, toScale;
        public float startAlpha;
        public bool active;
    }

    private Burst[] bursts;
    private int nextBurst;
    private SpriteRenderer[] paddleRenderers;
    private Color[] paddleBaseColors;
    private float[] paddleFlashLeft;
    private Collider2D lastCollider;
    private float lastHitTime = float.NegativeInfinity;

    private void Awake()
    {
        var root = new GameObject("ImpactFX").transform;   // scene root: bursts must not follow the ball

        bursts = new Burst[PoolSize];
        for (int i = 0; i < PoolSize; i++)
        {
            var go = new GameObject("Burst" + i);
            go.transform.SetParent(root, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = burstSprite;
            sr.sortingOrder = 1;
            go.SetActive(false);
            bursts[i].renderer = sr;
        }

        paddleRenderers = new SpriteRenderer[paddles.Length];
        paddleBaseColors = new Color[paddles.Length];
        paddleFlashLeft = new float[paddles.Length];
        for (int i = 0; i < paddles.Length; i++)
        {
            paddleRenderers[i] = paddles[i] != null ? paddles[i].GetComponent<SpriteRenderer>() : null;
            if (paddleRenderers[i] != null) paddleBaseColors[i] = paddleRenderers[i].color;
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        Collider2D other = collision.collider;
        if (other == lastCollider && Time.time - lastHitTime < sameColliderCooldown) return;

        Vector2 point = collision.contactCount > 0 ? collision.GetContact(0).point : (Vector2)transform.position;

        int paddleIndex = System.Array.IndexOf(paddles, other);
        if (paddleIndex >= 0)
        {
            paddleFlashLeft[paddleIndex] = paddleFlashSeconds;
            Color burstColor = theme != null ? theme.primary : Color.cyan;
            Spawn(point, Vector2.one * 0.25f, Vector2.one * paddleBurstSize, burstColor, 0.7f, paddleBurstSeconds);
        }
        else if (System.Array.IndexOf(walls, other) >= 0)
        {
            Color pulse = theme != null ? Color.Lerp(Color.white, theme.primary, 0.55f) : Color.white;   // pale cyan reads on the white wall
            Spawn(point, wallPulseSize * 0.6f, wallPulseSize, pulse, 0.45f, wallPulseSeconds);
        }
        else
        {
            return;
        }

        lastCollider = other;
        lastHitTime = Time.time;
    }

    private void Spawn(Vector2 position, Vector2 fromScale, Vector2 toScale, Color color, float alpha, float duration)
    {
        ref Burst b = ref bursts[nextBurst];
        nextBurst = (nextBurst + 1) % PoolSize;

        b.renderer.transform.position = new Vector3(position.x, position.y, 0f);
        b.renderer.color = new Color(color.r, color.g, color.b, alpha);
        b.fromScale = fromScale;
        b.toScale = toScale;
        b.startAlpha = alpha;
        b.elapsed = 0f;
        b.duration = duration;
        b.active = true;
        b.renderer.transform.localScale = new Vector3(fromScale.x, fromScale.y, 1f);
        b.renderer.gameObject.SetActive(true);
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        for (int i = 0; i < bursts.Length; i++)
        {
            if (!bursts[i].active) continue;

            ref Burst b = ref bursts[i];
            b.elapsed += dt;
            float t = Mathf.Clamp01(b.elapsed / b.duration);
            float eased = 1f - (1f - t) * (1f - t);
            Vector2 s = Vector2.Lerp(b.fromScale, b.toScale, eased);
            b.renderer.transform.localScale = new Vector3(s.x, s.y, 1f);
            Color c = b.renderer.color;
            c.a = b.startAlpha * (1f - t);
            b.renderer.color = c;
            if (t >= 1f)
            {
                b.active = false;
                b.renderer.gameObject.SetActive(false);
            }
        }

        Color flash = theme != null ? theme.highlight : Color.yellow;
        for (int i = 0; i < paddleRenderers.Length; i++)
        {
            if (paddleRenderers[i] == null || paddleFlashLeft[i] <= 0f) continue;

            paddleFlashLeft[i] = Mathf.Max(0f, paddleFlashLeft[i] - dt);
            float k = paddleFlashLeft[i] / paddleFlashSeconds;   // 1 -> 0
            paddleRenderers[i].color = Color.Lerp(paddleBaseColors[i], flash, k);
        }
    }
}
