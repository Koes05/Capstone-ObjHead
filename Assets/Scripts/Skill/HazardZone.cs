using UnityEngine;

// Legacy entry point kept so old scene/prefab references still deserialize.
[DisallowMultipleComponent]
public class HazardZone : MonoBehaviour
{
    public static GroundHazardZone Create(
        Vector2 position,
        float radius,
        int durationTurns,
        int damagePerTick,
        float unusedTickSeconds,
        float slowMultiplier,
        Color color,
        CharacterCombat owner,
        TurnManager manager)
    {
        return GroundHazardZone.Create(
            position,
            Mathf.Max(0.5f, radius * 2f),
            0.2f,
            Mathf.Max(1, durationTurns),
            damagePerTick,
            slowMultiplier,
            color,
            owner,
            manager);
    }
}
