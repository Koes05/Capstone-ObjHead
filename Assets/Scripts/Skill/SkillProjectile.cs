using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class SkillProjectile : MonoBehaviour
{
    private const int CircleTextureSize = 64;
    private const float MinimumKnockbackFalloff = 0.45f;
    private const float MinimumHorizontalComponent = 0.35f;
    private const float MinimumVerticalComponent = 0.2f;

    private static Sprite circleSprite;

    private CharacterCombat owner;
    private TurnManager turnManager;
    private ObjectHeadSkillSettings skillSettings;
    private float explosionFadeSeconds;
    private float remainingLifetime;
    private bool isCompleted;
    private Rigidbody2D body;
    private Vector2 lastVelocity;
    private TerrainManager terrain;
    private Vector2 previousPosition;
    private bool hasPreviousPosition;

    public void Initialize(
        Vector2 velocity,
        float radius,
        float gravityScale,
        float lifetime,
        Color color,
        CharacterCombat ownerCombat,
        TurnManager manager,
        int damage,
        float impactRadius,
        float fadeSeconds,
        Color fadeColor,
        float knockback,
        ObjectHeadSkillSettings settings)
    {
        body = GetComponent<Rigidbody2D>();
        CircleCollider2D circleCollider = GetComponent<CircleCollider2D>();
        SpriteRenderer spriteRenderer = GetComponent<SpriteRenderer>();

        owner = ownerCombat;
        turnManager = manager;
        explosionFadeSeconds = fadeSeconds;
        remainingLifetime = lifetime;
        lastVelocity = velocity;
        previousPosition = transform.position;
        hasPreviousPosition = true;
        terrain = FindTerrainManager();
        skillSettings = settings;

        if (skillSettings.explosionRadiusWorld <= 0f)
        {
            skillSettings.explosionRadiusWorld = impactRadius;
        }

        if (skillSettings.maxDamage <= 0)
        {
            skillSettings.maxDamage = damage;
        }

        if (skillSettings.knockbackForce <= 0f)
        {
            skillSettings.knockbackForce = knockback;
        }

        if (skillSettings.chainMaxTotalDamage <= 0)
        {
            skillSettings.chainMaxTotalDamage = Mathf.Max(skillSettings.maxDamage, skillSettings.maxDamage * Mathf.Max(1, skillSettings.chainCount));
        }

        if (skillSettings.projectileColor.a <= 0f)
        {
            skillSettings.projectileColor = color;
        }

        if (skillSettings.impactColor.a <= 0f)
        {
            skillSettings.impactColor = fadeColor;
        }

        ApplyProjectileVisual(spriteRenderer, radius);

        circleCollider.radius = 0.5f;

        body.gravityScale = gravityScale;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        body.linearVelocity = velocity;
    }

    private void FixedUpdate()
    {
        if (isCompleted || terrain == null || !hasPreviousPosition)
        {
            previousPosition = transform.position;
            hasPreviousPosition = true;
            return;
        }

        Vector2 currentPosition = transform.position;
        if (terrain.TryCheckTerrainHit(previousPosition, currentPosition, out TerrainHit hit))
        {
            ResolveImpact(hit.point);
            return;
        }

        previousPosition = currentPosition;
    }

    private void Update()
    {
        if (isCompleted)
        {
            return;
        }

        if (body != null && body.linearVelocity.sqrMagnitude > 0.001f)
        {
            lastVelocity = body.linearVelocity;
        }

        remainingLifetime -= Time.deltaTime;

        if (remainingLifetime <= 0f)
        {
            ResolveImpact(transform.position);
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (isCompleted)
        {
            return;
        }

        Vector2 impactPoint = collision.contactCount > 0
            ? collision.GetContact(0).point
            : (Vector2)transform.position;
        ResolveImpact(impactPoint);
    }

    private void ResolveImpact(Vector2 impactPoint)
    {
        if (isCompleted)
        {
            return;
        }

        isCompleted = true;

        if (skillSettings.effectType == SkillEffectType.DelayedExplosion && skillSettings.delaySeconds > 0f)
        {
            StartCoroutine(DelayedImpactRoutine(impactPoint));
            return;
        }

        if (skillSettings.blinkBeforeEffect)
        {
            StartCoroutine(BlinkThenImpactRoutine(impactPoint));
            return;
        }

        ApplySkillEffect(impactPoint);
        StartCoroutine(ExplosionFadeRoutine(impactPoint));
    }

    private IEnumerator DelayedImpactRoutine(Vector2 impactPoint)
    {
        DisablePhysicsAtImpact(impactPoint);

        SpriteRenderer spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            spriteRenderer.color = skillSettings.projectileColor;
        }

        yield return new WaitForSeconds(skillSettings.delaySeconds);

        ApplySkillEffect(impactPoint);
        StartCoroutine(ExplosionFadeRoutine(impactPoint));
    }

    private IEnumerator BlinkThenImpactRoutine(Vector2 impactPoint)
    {
        DisablePhysicsAtImpact(impactPoint);

        SpriteRenderer spriteRenderer = GetComponent<SpriteRenderer>();
        float interval = Mathf.Max(0.03f, skillSettings.blinkIntervalSeconds);
        float duration = Mathf.Max(interval, skillSettings.blinkSeconds);
        float elapsed = 0f;
        bool useA = true;

        while (elapsed < duration)
        {
            if (spriteRenderer != null)
            {
                Sprite nextSprite = useA ? skillSettings.blinkSpriteA : skillSettings.blinkSpriteB;
                if (nextSprite != null)
                {
                    spriteRenderer.sprite = nextSprite;
                }

                spriteRenderer.color = useA ? Color.white : skillSettings.projectileColor;
            }

            useA = !useA;
            elapsed += interval;
            yield return new WaitForSeconds(interval);
        }

        ApplySkillEffect(impactPoint);
        StartCoroutine(ExplosionFadeRoutine(impactPoint));
    }

    private void ApplySkillEffect(Vector2 impactPoint)
    {
        float fallbackHorizontalSign = Mathf.Abs(lastVelocity.x) > 0.001f ? Mathf.Sign(lastVelocity.x) : 0f;

        switch (skillSettings.effectType)
        {
            case SkillEffectType.CreateTerrainCircle:
                CreateTerrainCircle(impactPoint);
                ApplyDamage(impactPoint, fallbackHorizontalSign);
                break;
            case SkillEffectType.CreateTerrainBridge:
                CreateTerrainBridge(impactPoint);
                ApplyDamage(impactPoint, fallbackHorizontalSign);
                break;
            case SkillEffectType.CreateHazardZone:
            case SkillEffectType.CreateSlowZone:
                CreateHazardZone(impactPoint);
                ApplyDamage(impactPoint, fallbackHorizontalSign);
                break;
            case SkillEffectType.ChainExplosion:
                ApplyChainExplosion(impactPoint, fallbackHorizontalSign);
                break;
            case SkillEffectType.DelayedExplosion:
            case SkillEffectType.DamageExplosion:
            default:
                DestroyTerrainAtImpact(impactPoint);
                ApplyDamage(impactPoint, fallbackHorizontalSign);
                break;
        }
    }

    private void ApplyDamage(Vector2 impactPoint, float fallbackHorizontalSign)
    {
        DamageSystem.ApplyExplosion(
            impactPoint,
            owner,
            skillSettings.explosionRadiusWorld,
            skillSettings.maxDamage,
            skillSettings.knockbackForce,
            fallbackHorizontalSign);
    }

    private void ApplyChainExplosion(Vector2 impactPoint, float fallbackHorizontalSign)
    {
        int count = Mathf.Max(1, skillSettings.chainCount);
        Vector2 spreadDirection = GetHorizontalImpactDirection();
        Vector2 perpendicular = new Vector2(-spreadDirection.y, spreadDirection.x);
        Vector2[] explosionPoints = new Vector2[count];

        for (int i = 0; i < count; i++)
        {
            float centeredIndex = i - (count - 1) * 0.5f;
            Vector2 point = impactPoint
                + spreadDirection * (centeredIndex * skillSettings.chainSpacingWorld)
                + perpendicular * (Mathf.Sin(i * 1.7f) * skillSettings.chainSpacingWorld * 0.5f);

            explosionPoints[i] = point;
            DestroyTerrainAtImpact(point);
            SpawnExplosionMarker(point);
        }

        ApplyCappedChainDamage(explosionPoints, fallbackHorizontalSign);
    }

    private void ApplyCappedChainDamage(Vector2[] explosionPoints, float fallbackHorizontalSign)
    {
        if (explosionPoints == null || explosionPoints.Length == 0 || skillSettings.maxDamage <= 0 || skillSettings.explosionRadiusWorld <= 0f)
        {
            return;
        }

        int damageCap = Mathf.Max(skillSettings.maxDamage, skillSettings.chainMaxTotalDamage);
        Dictionary<CharacterCombat, int> damageByCharacter = new Dictionary<CharacterCombat, int>();
        Dictionary<CharacterCombat, Vector2> forceByCharacter = new Dictionary<CharacterCombat, Vector2>();

        for (int i = 0; i < explosionPoints.Length; i++)
        {
            Vector2 center = explosionPoints[i];
            Collider2D[] hits = Physics2D.OverlapCircleAll(center, skillSettings.explosionRadiusWorld);
            HashSet<CharacterCombat> damagedAtThisPoint = new HashSet<CharacterCombat>();

            for (int hitIndex = 0; hitIndex < hits.Length; hitIndex++)
            {
                Collider2D hit = hits[hitIndex];
                if (hit == null)
                {
                    continue;
                }

                CharacterCombat combat = hit.GetComponentInParent<CharacterCombat>();
                if (combat == null || combat.IsDead || !damagedAtThisPoint.Add(combat))
                {
                    continue;
                }

                Vector2 characterCenter = combat.KnockbackCenter;
                Vector2 impactToCharacter = characterCenter - center;
                float distance = impactToCharacter.magnitude;
                float distanceRatio = Mathf.Clamp01(distance / skillSettings.explosionRadiusWorld);
                float falloff = 1f - distanceRatio;
                int damage = Mathf.CeilToInt(skillSettings.maxDamage * falloff);
                if (damage <= 0)
                {
                    continue;
                }

                damageByCharacter.TryGetValue(combat, out int currentDamage);
                damageByCharacter[combat] = Mathf.Min(damageCap, currentDamage + damage);

                Vector2 knockbackDirection = CalculateKnockbackDirection(center, characterCenter, impactToCharacter, fallbackHorizontalSign);
                float curvedFalloff = Mathf.Pow(falloff, 0.75f);
                float knockbackFalloff = Mathf.Lerp(MinimumKnockbackFalloff, 1f, curvedFalloff);
                Vector2 force = knockbackDirection * skillSettings.knockbackForce * knockbackFalloff;

                forceByCharacter.TryGetValue(combat, out Vector2 currentForce);
                forceByCharacter[combat] = currentForce + force;
            }
        }

        foreach (KeyValuePair<CharacterCombat, int> pair in damageByCharacter)
        {
            CharacterCombat combat = pair.Key;
            if (combat == null || combat.IsDead)
            {
                continue;
            }

            forceByCharacter.TryGetValue(combat, out Vector2 force);
            float maxForce = Mathf.Max(skillSettings.knockbackForce, skillSettings.knockbackForce * 1.45f);
            if (force.magnitude > maxForce)
            {
                force = force.normalized * maxForce;
            }

            combat.ApplyKnockback(force);
            combat.TakeDamage(pair.Value);
        }
    }

    private Vector2 CalculateKnockbackDirection(Vector2 center, Vector2 characterCenter, Vector2 impactToCharacter, float fallbackHorizontalSign)
    {
        if (impactToCharacter.sqrMagnitude < 0.001f)
        {
            impactToCharacter = Vector2.up;
        }

        Vector2 knockbackDirection = impactToCharacter.normalized;
        if (Mathf.Abs(knockbackDirection.x) < MinimumHorizontalComponent)
        {
            float horizontalOffset = characterCenter.x - center.x;
            float horizontalSign = Mathf.Abs(horizontalOffset) > 0.001f
                ? Mathf.Sign(horizontalOffset)
                : Mathf.Abs(fallbackHorizontalSign) > 0.001f
                    ? Mathf.Sign(fallbackHorizontalSign)
                    : 1f;

            knockbackDirection.x = MinimumHorizontalComponent * horizontalSign;
            knockbackDirection.Normalize();
        }

        if (Mathf.Abs(knockbackDirection.y) < MinimumVerticalComponent)
        {
            knockbackDirection.y = MinimumVerticalComponent * (knockbackDirection.y < 0f ? -1f : 1f);
            knockbackDirection.Normalize();
        }

        return knockbackDirection;
    }

    private void SpawnExplosionMarker(Vector2 point)
    {
        GameObject marker = new GameObject("ChainExplosionMarker");
        marker.transform.position = point;
        SpriteRenderer renderer = marker.AddComponent<SpriteRenderer>();
        renderer.sprite = GetCircleSprite();
        renderer.color = skillSettings.impactColor;
        renderer.sortingOrder = 39;
        marker.transform.localScale = Vector3.one * Mathf.Max(0.01f, skillSettings.explosionRadiusWorld * 2f);
        StartCoroutine(FadeAndDestroyMarker(marker, renderer));
    }

    private IEnumerator FadeAndDestroyMarker(GameObject marker, SpriteRenderer renderer)
    {
        float duration = Mathf.Max(0.08f, explosionFadeSeconds);
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            if (renderer != null)
            {
                Color color = skillSettings.impactColor;
                color.a *= 1f - elapsed / duration;
                renderer.color = color;
            }

            yield return null;
        }

        if (marker != null)
        {
            Destroy(marker);
        }
    }

    private void DestroyTerrainAtImpact(Vector2 impactPoint)
    {
        TerrainManager terrainManager = GetTerrain();
        if (terrainManager == null)
        {
            return;
        }

        int radiusPx = skillSettings.terrainRadiusPx > 0
            ? skillSettings.terrainRadiusPx
            : Mathf.Max(1, Mathf.RoundToInt(skillSettings.explosionRadiusWorld * terrainManager.PixelsPerUnit));
        terrainManager.DestroyCircle(impactPoint, radiusPx);
    }

    private void CreateTerrainCircle(Vector2 impactPoint)
    {
        TerrainManager terrainManager = GetTerrain();
        if (terrainManager == null)
        {
            return;
        }

        int radiusPx = skillSettings.terrainRadiusPx > 0
            ? skillSettings.terrainRadiusPx
            : Mathf.Max(1, Mathf.RoundToInt(skillSettings.explosionRadiusWorld * terrainManager.PixelsPerUnit));
        terrainManager.CreateCircle(impactPoint, radiusPx, TerrainType.Created, FindBlockedCharacterColliders());
    }

    private void CreateTerrainBridge(Vector2 impactPoint)
    {
        TerrainManager terrainManager = GetTerrain();
        if (terrainManager == null)
        {
            return;
        }

        Vector2 direction = GetHorizontalImpactDirection();
        float length = Mathf.Max(0.5f, skillSettings.bridgeLengthWorld);
        int thicknessPx = Mathf.Max(1, skillSettings.bridgeThicknessPx);
        terrainManager.CreateBridge(impactPoint, direction, length, thicknessPx);
    }

    private void CreateHazardZone(Vector2 impactPoint)
    {
        if (skillSettings.zoneDurationSeconds <= 0f || skillSettings.zoneDamagePerTick <= 0)
        {
            return;
        }

        HazardZone.Create(
            impactPoint,
            skillSettings.explosionRadiusWorld,
            skillSettings.zoneDurationSeconds,
            skillSettings.zoneDamagePerTick,
            skillSettings.zoneTickSeconds,
            skillSettings.slowMultiplier,
            skillSettings.impactColor,
            owner);
    }

    private Collider2D[] FindBlockedCharacterColliders()
    {
#if UNITY_6000_0_OR_NEWER || UNITY_2023_1_OR_NEWER
        CharacterCombat[] combats = Object.FindObjectsByType<CharacterCombat>(FindObjectsSortMode.None);
#else
        CharacterCombat[] combats = Object.FindObjectsOfType<CharacterCombat>();
#endif
        Collider2D[] colliders = new Collider2D[combats.Length];
        for (int i = 0; i < combats.Length; i++)
        {
            colliders[i] = combats[i] != null ? combats[i].GetComponent<Collider2D>() : null;
        }

        return colliders;
    }

    private Vector2 GetHorizontalImpactDirection()
    {
        float sign = Mathf.Abs(lastVelocity.x) > 0.001f ? Mathf.Sign(lastVelocity.x) : 1f;
        return new Vector2(sign, 0.15f).normalized;
    }

    private void CompleteTurn()
    {
        if (!isCompleted)
        {
            isCompleted = true;
        }

        if (turnManager != null)
        {
            turnManager.EndTurn();
        }
    }

    private IEnumerator ExplosionFadeRoutine(Vector2 impactPoint)
    {
        DisablePhysicsAtImpact(impactPoint);

        SpriteRenderer spriteRenderer = GetComponent<SpriteRenderer>();
        transform.position = impactPoint;
        transform.localScale = Vector3.one * Mathf.Max(0.01f, skillSettings.explosionRadiusWorld * 2f);

        if (spriteRenderer != null)
        {
            spriteRenderer.sprite = GetCircleSprite();
            spriteRenderer.sortingOrder = 40;
        }

        CompleteTurn();

        float duration = Mathf.Max(0.01f, explosionFadeSeconds);
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            SetExplosionAlpha(spriteRenderer, 1f - elapsed / duration);
            yield return null;
        }

        Destroy(gameObject);
    }

    private void DisablePhysicsAtImpact(Vector2 impactPoint)
    {
        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.simulated = false;
        }

        Collider2D projectileCollider = GetComponent<Collider2D>();
        if (projectileCollider != null)
        {
            projectileCollider.enabled = false;
        }

        transform.position = impactPoint;
    }

    private void ApplyProjectileVisual(SpriteRenderer spriteRenderer, float radius)
    {
        Sprite sprite = skillSettings.headSprite != null ? skillSettings.headSprite : GetCircleSprite();
        spriteRenderer.sprite = sprite;
        spriteRenderer.color = skillSettings.projectileColor;
        spriteRenderer.sortingOrder = 30;

        float visualDiameter = skillSettings.projectileVisualDiameter > 0f
            ? skillSettings.projectileVisualDiameter
            : Mathf.Max(0.01f, radius * 2f);
        float spriteDiameter = sprite != null ? Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y) : 1f;
        transform.localScale = Vector3.one * Mathf.Max(0.01f, visualDiameter / Mathf.Max(0.01f, spriteDiameter));
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

        circleSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, CircleTextureSize, CircleTextureSize),
            new Vector2(0.5f, 0.5f),
            CircleTextureSize);
        circleSprite.hideFlags = HideFlags.HideAndDontSave;
        return circleSprite;
    }

    private void SetExplosionAlpha(SpriteRenderer spriteRenderer, float alphaMultiplier)
    {
        if (spriteRenderer == null)
        {
            return;
        }

        Color color = skillSettings.impactColor;
        color.a *= Mathf.Clamp01(alphaMultiplier);
        spriteRenderer.color = color;
    }

    private TerrainManager GetTerrain()
    {
        if (terrain == null)
        {
            terrain = FindTerrainManager();
        }

        return terrain;
    }

    private TerrainManager FindTerrainManager()
    {
#if UNITY_6000_0_OR_NEWER || UNITY_2023_1_OR_NEWER
        return Object.FindAnyObjectByType<TerrainManager>();
#else
        return Object.FindObjectOfType<TerrainManager>();
#endif
    }
}
