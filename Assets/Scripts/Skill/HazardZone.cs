using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(CircleCollider2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class HazardZone : MonoBehaviour
{
    private const int CircleTextureSize = 64;
    private static Sprite circleSprite;

    private readonly Dictionary<CharacterCombat, float> nextDamageTimes = new Dictionary<CharacterCombat, float>();
    private CharacterCombat owner;
    private int damagePerTick;
    private float tickSeconds;
    private float slowMultiplier = 1f;
    private float remainingSeconds;
    private Color baseColor;
    private SpriteRenderer spriteRenderer;

    public static HazardZone Create(
        Vector2 position,
        float radius,
        float durationSeconds,
        int damagePerTick,
        float tickSeconds,
        float slowMultiplier,
        Color color,
        CharacterCombat owner)
    {
        GameObject zoneObject = new GameObject("HazardZone");
        zoneObject.transform.position = position;
        zoneObject.transform.localScale = Vector3.one * Mathf.Max(0.05f, radius * 2f);

        HazardZone zone = zoneObject.AddComponent<HazardZone>();
        zone.Initialize(durationSeconds, damagePerTick, tickSeconds, slowMultiplier, color, owner);
        return zone;
    }

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        spriteRenderer.sprite = GetCircleSprite();
        spriteRenderer.sortingOrder = 20;

        CircleCollider2D circleCollider = GetComponent<CircleCollider2D>();
        circleCollider.isTrigger = true;
        circleCollider.radius = 0.5f;
    }

    public void Initialize(float durationSeconds, int tickDamage, float tickIntervalSeconds, float movementSlowMultiplier, Color color, CharacterCombat zoneOwner)
    {
        owner = zoneOwner;
        damagePerTick = Mathf.Max(0, tickDamage);
        tickSeconds = Mathf.Max(0.1f, tickIntervalSeconds);
        slowMultiplier = Mathf.Clamp(movementSlowMultiplier, 0.1f, 1f);
        remainingSeconds = Mathf.Max(0.1f, durationSeconds);
        baseColor = color;

        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        spriteRenderer.color = baseColor;
    }

    private void Update()
    {
        remainingSeconds -= Time.deltaTime;

        if (spriteRenderer != null)
        {
            Color color = baseColor;
            color.a *= Mathf.Clamp01(remainingSeconds / 0.75f);
            spriteRenderer.color = color;
        }

        if (remainingSeconds <= 0f)
        {
            Destroy(gameObject);
        }
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        CharacterCombat combat = other.GetComponentInParent<CharacterCombat>();
        if (combat == null || combat == owner || combat.IsDead)
        {
            return;
        }

        if (slowMultiplier < 0.999f)
        {
            TurnCharacterController controller = combat.GetComponent<TurnCharacterController>();
            controller?.ApplyMoveSpeedMultiplier(slowMultiplier, tickSeconds + 0.05f);
        }

        if (damagePerTick <= 0)
        {
            return;
        }

        if (!nextDamageTimes.TryGetValue(combat, out float nextTime))
        {
            nextTime = 0f;
        }

        if (Time.time < nextTime)
        {
            return;
        }

        combat.TakeDamage(damagePerTick);
        nextDamageTimes[combat] = Time.time + tickSeconds;
    }

    private static Sprite GetCircleSprite()
    {
        if (circleSprite != null)
        {
            return circleSprite;
        }

        Texture2D texture = new Texture2D(CircleTextureSize, CircleTextureSize, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;
        Vector2 center = new Vector2((CircleTextureSize - 1) * 0.5f, (CircleTextureSize - 1) * 0.5f);
        float radius = CircleTextureSize * 0.48f;

        for (int y = 0; y < CircleTextureSize; y++)
        {
            for (int x = 0; x < CircleTextureSize; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                float alpha = Mathf.Clamp01(radius - distance);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply();
        texture.hideFlags = HideFlags.HideAndDontSave;

        circleSprite = Sprite.Create(texture, new Rect(0f, 0f, CircleTextureSize, CircleTextureSize), new Vector2(0.5f, 0.5f), CircleTextureSize);
        circleSprite.hideFlags = HideFlags.HideAndDontSave;
        return circleSprite;
    }
}
