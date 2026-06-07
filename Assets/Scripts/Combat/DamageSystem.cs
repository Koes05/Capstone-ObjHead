using System.Collections.Generic;
using UnityEngine;

public static class DamageSystem
{
    private const float MinimumKnockbackFalloff = 0.45f;
    private const float MinimumHorizontalComponent = 0.35f;
    private const float MinimumVerticalComponent = 0.2f;

    public static void ApplyExplosion(Vector2 center, CharacterCombat owner, float radius, int maxDamage, float knockbackForce, float fallbackHorizontalSign = 0f)
    {
        if (radius <= 0f || maxDamage <= 0)
        {
            return;
        }

        Collider2D[] hits = Physics2D.OverlapCircleAll(center, radius);
        HashSet<CharacterCombat> damagedCharacters = new HashSet<CharacterCombat>();

        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hit = hits[i];
            if (hit == null)
            {
                continue;
            }

            CharacterCombat combat = hit.GetComponentInParent<CharacterCombat>();
            if (combat == null || combat.IsDead || !damagedCharacters.Add(combat))
            {
                continue;
            }

            Vector2 characterCenter = combat.KnockbackCenter;
            Vector2 impactToCharacter = characterCenter - center;
            float distance = impactToCharacter.magnitude;
            float distanceRatio = Mathf.Clamp01(distance / radius);
            float falloff = 1f - distanceRatio;
            int damage = Mathf.CeilToInt(maxDamage * falloff);

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
                        : 0f;

                if (Mathf.Abs(horizontalSign) < 0.001f)
                {
                    horizontalSign = 1f;
                }

                knockbackDirection.x = MinimumHorizontalComponent * horizontalSign;
                knockbackDirection.Normalize();
            }

            if (Mathf.Abs(knockbackDirection.y) < MinimumVerticalComponent)
            {
                knockbackDirection.y = MinimumVerticalComponent * (knockbackDirection.y < 0f ? -1f : 1f);
                knockbackDirection.Normalize();
            }

            float curvedFalloff = Mathf.Pow(falloff, 0.75f);
            float knockbackFalloff = Mathf.Lerp(MinimumKnockbackFalloff, 1f, curvedFalloff);
            combat.ApplyKnockback(knockbackDirection * knockbackForce * knockbackFalloff);
            combat.TakeDamage(damage);
        }
    }
}
