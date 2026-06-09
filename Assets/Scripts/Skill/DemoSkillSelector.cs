using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public enum SkillEffectType
{
    DamageExplosion,
    DelayedExplosion,
    ChainExplosion,
    CreateTerrainCircle,
    CreateTerrainBridge,
    CreateHazardZone,
    CreateSlowZone
}

public struct ObjectHeadSkillSettings
{
    public SkillEffectType effectType;
    public Sprite headSprite;
    public Color projectileColor;
    public Color impactColor;
    public int maxDamage;
    public float explosionRadiusWorld;
    public float knockbackForce;
    public int terrainRadiusPx;
    public float bridgeLengthWorld;
    public int bridgeThicknessPx;
    public int chainCount;
    public float chainSpacingWorld;
    public float chainDelaySeconds;
    public int chainMaxTotalDamage;
    public float chainSpreadRadiusWorld;
    public bool useWideClusterPattern;
    public bool useRollingChainPath;
    public float rollingChainMinSpeed;
    public float rollingChainAngularSpeed;
    public float delaySeconds;
    public int zoneDurationTurns;
    public int zoneDamagePerTick;
    public float zoneTickSeconds;
    public int zoneDurationRounds;
    public int zoneDamagePerTurn;
    public float zoneLengthWorld;
    public float zoneThicknessWorld;
    public float slowMultiplier;
    public float projectileVisualDiameter;
    public bool blinkBeforeEffect;
    public float blinkSeconds;
    public float blinkIntervalSeconds;
    public Sprite blinkSpriteA;
    public Sprite blinkSpriteB;
    public int skillId;
    public int commonHeadTypeId;
    public int terrainBurstCount;
    public int terrainBurstStampRadiusPx;
    public int terrainBurstMaxPlacementAttemptsPerStamp;
    public float terrainBurstIntervalSeconds;
    public float terrainBurstSpreadWorld;
    public float terrainBurstVerticalBiasWorld;
    public float finalTerrainRadiusXWorld;
    public float finalTerrainRadiusYWorld;
    public float maxBuildHeightAboveSurfaceWorld;

    public static ObjectHeadSkillSettings CreateDefault(
        Sprite headSprite,
        Color projectileColor,
        Color impactColor,
        int maxDamage,
        float explosionRadiusWorld,
        float knockbackForce)
    {
        return new ObjectHeadSkillSettings
        {
            effectType = SkillEffectType.DamageExplosion,
            headSprite = headSprite,
            projectileColor = projectileColor,
            impactColor = impactColor,
            maxDamage = maxDamage,
            explosionRadiusWorld = explosionRadiusWorld,
            knockbackForce = knockbackForce,
            terrainRadiusPx = 0,
            bridgeLengthWorld = 0f,
            bridgeThicknessPx = 8,
            chainCount = 1,
            chainSpacingWorld = 0.25f,
            chainDelaySeconds = 0.1f,
            chainMaxTotalDamage = maxDamage,
            chainSpreadRadiusWorld = 0f,
            useWideClusterPattern = false,
            useRollingChainPath = false,
            rollingChainMinSpeed = 0f,
            rollingChainAngularSpeed = 0f,
            delaySeconds = 0f,
            zoneDurationTurns = 0,
            zoneDamagePerTick = 0,
            zoneTickSeconds = 1f,
            zoneDurationRounds = 0,
            zoneDamagePerTurn = 0,
            zoneLengthWorld = 0f,
            zoneThicknessWorld = 0.2f,
            slowMultiplier = 1f,
            projectileVisualDiameter = 0.58f,
            blinkBeforeEffect = false,
            blinkSeconds = 0.45f,
            blinkIntervalSeconds = 0.08f,
            blinkSpriteA = null,
            blinkSpriteB = null,
            skillId = 0,
            commonHeadTypeId = 0,
            terrainBurstCount = 0,
            terrainBurstStampRadiusPx = 0,
            terrainBurstMaxPlacementAttemptsPerStamp = 4,
            terrainBurstIntervalSeconds = 0.06f,
            terrainBurstSpreadWorld = 1f,
            terrainBurstVerticalBiasWorld = 0f,
            finalTerrainRadiusXWorld = 1f,
            finalTerrainRadiusYWorld = 0.8f,
            maxBuildHeightAboveSurfaceWorld = 5f
        };
    }
}

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterVisual))]
public class DemoSkillSelector : MonoBehaviour
{
    [SerializeField] private ObjectHeadCharacterKind characterKind = ObjectHeadCharacterKind.Bulb;
    [SerializeField, Range(0, 2)] private int selectedSkillIndex;
    [SerializeField] private bool allowKeyboardSelection = true;

    [Header("Seed Terrain Growth")]
    [SerializeField, Min(1)] private int seedTerrainBurstCount = 12;
    [SerializeField, Min(1)] private int seedTerrainBurstStampRadiusPx = 9;
    [SerializeField, Min(0.01f)] private float seedTerrainBurstIntervalSeconds = 0.055f;
    [SerializeField, Min(1)] private int terrainBurstMaxPlacementAttemptsPerStamp = 4;
    [SerializeField, Min(0.1f)] private float seedTerrainRadiusXWorld = 1.2f;
    [SerializeField, Min(0.5f)] private float seedTerrainRadiusYWorld = 0.95f;
    [SerializeField, Min(0.5f)] private float minimumCreatedTerrainRadiusYWorld = 0.5f;
    [SerializeField, Min(0.5f)] private float maxBuildHeightAboveSurfaceWorld = 5f;

    private readonly int[] remainingCooldowns = new int[3];
    private CharacterVisual characterVisual;
    private TurnCharacterController turnCharacter;
    private CommonHeadUseController commonHeadUseController;

    public ObjectHeadCharacterKind CharacterKind => characterKind;
    public int SelectedSkillIndex => selectedSkillIndex;

    private void Awake()
    {
        characterVisual = GetComponent<CharacterVisual>();
        turnCharacter = GetComponent<TurnCharacterController>();
        commonHeadUseController = GetComponent<CommonHeadUseController>();
        ApplySelection();
    }

    private void Update()
    {
        if (!allowKeyboardSelection || turnCharacter == null || !turnCharacter.HasControl)
        {
            return;
        }

        TurnManager manager = FindTurnManager();
        if (manager == null || !manager.CanCharacterFire(turnCharacter))
        {
            return;
        }

        ReadSelectionInput();
    }

    public void SetCharacterKind(ObjectHeadCharacterKind kind)
    {
        CancelCommonHeadSelection();
        characterKind = kind;
        ApplySelection();
    }

    public void SetSkillIndex(int index)
    {
        CancelCommonHeadSelection();
        selectedSkillIndex = Mathf.Clamp(index, 0, 2);
        ApplySelection();
    }

    public bool CanUseSelectedSkill()
    {
        return GetRemainingCooldown(selectedSkillIndex) <= 0;
    }

    public int GetRemainingCooldown(int skillIndex)
    {
        return remainingCooldowns[Mathf.Clamp(skillIndex, 0, 2)];
    }

    public int GetCooldownDuration(int skillIndex)
    {
        skillIndex = Mathf.Clamp(skillIndex, 0, 2);
        if (skillIndex == 0) return 0;
        if (skillIndex == 1) return 2;
        return 3;
    }

    public void NotifyTurnStarted()
    {
        for (int i = 0; i < remainingCooldowns.Length; i++)
        {
            remainingCooldowns[i] = Mathf.Max(0, remainingCooldowns[i] - 1);
        }

        if (!CanUseSelectedSkill())
        {
            SelectFirstReadySkill();
        }
    }

    public void NotifySkillFired()
    {
        remainingCooldowns[selectedSkillIndex] = GetCooldownDuration(selectedSkillIndex);
    }

    public ObjectHeadSkillSettings GetCurrentSkillSettings()
    {
        Sprite headSprite = characterVisual != null ? characterVisual.CurrentHeadSprite : null;
        ObjectHeadSkillSettings settings = ObjectHeadSkillSettings.CreateDefault(
            headSprite,
            new Color(1f, 0.35f, 0.05f, 1f),
            new Color(1f, 0.25f, 0f, 0.55f),
            20,
            0.8f,
            7f);

        switch (characterKind)
        {
            case ObjectHeadCharacterKind.Bulb:
                ConfigureBulbSkill(ref settings);
                break;
            case ObjectHeadCharacterKind.Seed:
                ConfigureSeedSkill(ref settings);
                break;
            case ObjectHeadCharacterKind.Bomb:
                ConfigureBombSkill(ref settings);
                break;
        }

        settings.skillId = ((int)characterKind + 1) * 10 + selectedSkillIndex + 1;
        settings.headSprite = headSprite;
        return settings;
    }

    private void ConfigureBulbSkill(ref ObjectHeadSkillSettings settings)
    {
        settings.projectileColor = new Color(0.85f, 0.82f, 0.68f, 1f);
        settings.impactColor = new Color(1f, 0.95f, 0.45f, 0.5f);
        settings.projectileVisualDiameter = 0.62f;

        if (selectedSkillIndex == 0)
        {
            settings.effectType = SkillEffectType.CreateHazardZone;
            settings.maxDamage = 5;
            settings.explosionRadiusWorld = 0.7f;
            settings.knockbackForce = 0f;
            settings.zoneDamagePerTurn = 8;
            settings.zoneDurationRounds = 2;
            settings.zoneLengthWorld = 4.5f;
            settings.zoneThicknessWorld = 0.18f;
            settings.slowMultiplier = 1f;
            return;
        }

        if (selectedSkillIndex == 1)
        {
            settings.effectType = SkillEffectType.CreateSlowZone;
            settings.maxDamage = 15;
            settings.explosionRadiusWorld = 1.4f;
            settings.knockbackForce = 2.5f;
            settings.terrainRadiusPx = 0;
            settings.zoneDamagePerTurn = 10;
            settings.zoneDurationRounds = 2;
            settings.zoneLengthWorld = 6f;
            settings.zoneThicknessWorld = 0.22f;
            settings.slowMultiplier = 0.6f;
            settings.impactColor = new Color(1f, 0.9f, 0.15f, 0.48f);
            return;
        }

        settings.effectType = SkillEffectType.ChainExplosion;
        settings.maxDamage = 8;
        settings.chainMaxTotalDamage = 35;
        settings.explosionRadiusWorld = 0.7f;
        settings.knockbackForce = 4f;
        settings.terrainRadiusPx = 17;
        settings.chainCount = 5;
        settings.chainSpacingWorld = 0.4f;
        settings.chainDelaySeconds = 0.1f;
        settings.blinkBeforeEffect = true;
        settings.blinkSeconds = 0.7f;
        settings.blinkIntervalSeconds = 0.085f;
        settings.blinkSpriteA = Resources.Load<Sprite>("Sprites/Heads/head_bulb_on");
        settings.blinkSpriteB = Resources.Load<Sprite>("Sprites/Heads/head_bulb_off");
    }

    private void ConfigureSeedSkill(ref ObjectHeadSkillSettings settings)
    {
        settings.projectileColor = new Color(0.36f, 0.62f, 0.28f, 1f);
        settings.impactColor = new Color(0.35f, 0.9f, 0.35f, 0.5f);
        settings.knockbackForce = 2f;
        settings.projectileVisualDiameter = 0.58f;

        if (selectedSkillIndex == 0)
        {
            settings.effectType = SkillEffectType.CreateTerrainCircle;
            settings.maxDamage = 5;
            settings.explosionRadiusWorld = 0.48f;
            settings.terrainRadiusPx = 18;
            settings.terrainBurstCount = seedTerrainBurstCount;
            settings.terrainBurstStampRadiusPx = seedTerrainBurstStampRadiusPx;
            settings.terrainBurstMaxPlacementAttemptsPerStamp = terrainBurstMaxPlacementAttemptsPerStamp;
            settings.terrainBurstIntervalSeconds = seedTerrainBurstIntervalSeconds;
            settings.terrainBurstSpreadWorld = seedTerrainRadiusXWorld;
            settings.terrainBurstVerticalBiasWorld = 0f;
            settings.finalTerrainRadiusXWorld = seedTerrainRadiusXWorld;
            settings.finalTerrainRadiusYWorld = Mathf.Max(
                minimumCreatedTerrainRadiusYWorld,
                seedTerrainRadiusYWorld);
            settings.maxBuildHeightAboveSurfaceWorld = maxBuildHeightAboveSurfaceWorld;
            return;
        }

        if (selectedSkillIndex == 1)
        {
            settings.effectType = SkillEffectType.CreateTerrainBridge;
            settings.maxDamage = 3;
            settings.explosionRadiusWorld = 0.38f;
            settings.bridgeLengthWorld = 6.5f;
            settings.bridgeThicknessPx = 9;
            return;
        }

        settings.effectType = SkillEffectType.CreateHazardZone;
        settings.maxDamage = 10;
        settings.explosionRadiusWorld = 0.8f;
        settings.zoneDamagePerTurn = 12;
        settings.zoneDurationRounds = 2;
        settings.zoneLengthWorld = 5.5f;
        settings.zoneThicknessWorld = 0.25f;
        settings.slowMultiplier = 1f;
        settings.impactColor = new Color(0.24f, 0.78f, 0.24f, 0.48f);
    }

    private void ConfigureBombSkill(ref ObjectHeadSkillSettings settings)
    {
        settings.projectileColor = new Color(1f, 0.75f, 0.05f, 1f);
        settings.impactColor = new Color(1f, 0.15f, 0f, 0.58f);
        settings.projectileVisualDiameter = 0.62f;

        if (selectedSkillIndex == 0)
        {
            settings.effectType = SkillEffectType.DamageExplosion;
            settings.maxDamage = 15;
            settings.explosionRadiusWorld = 1.1f;
            settings.terrainRadiusPx = 26;
            settings.knockbackForce = 12f;
            return;
        }

        if (selectedSkillIndex == 1)
        {
            settings.effectType = SkillEffectType.DelayedExplosion;
            settings.maxDamage = 30;
            settings.explosionRadiusWorld = 1.4f;
            settings.terrainRadiusPx = 40;
            settings.knockbackForce = 7f;
            settings.delaySeconds = 2f;
            settings.impactColor = new Color(1f, 0.05f, 0.02f, 0.58f);
            return;
        }

        settings.effectType = SkillEffectType.ChainExplosion;
        settings.maxDamage = 9;
        settings.chainMaxTotalDamage = 45;
        settings.explosionRadiusWorld = 0.9f;
        settings.terrainRadiusPx = 28;
        settings.knockbackForce = 7f;
        settings.chainCount = 6;
        settings.chainSpacingWorld = 0.5f;
        settings.chainDelaySeconds = 0.14f;
        settings.useRollingChainPath = true;
        settings.rollingChainMinSpeed = 3.2f;
        settings.rollingChainAngularSpeed = 720f;
        settings.impactColor = new Color(1f, 0.85f, 0.05f, 0.55f);
    }

    private void ApplySelection()
    {
        selectedSkillIndex = Mathf.Clamp(selectedSkillIndex, 0, 2);

        if (characterVisual == null)
        {
            characterVisual = GetComponent<CharacterVisual>();
        }

        if (characterVisual != null)
        {
            characterVisual.SetCharacterKind(characterKind);
            characterVisual.SetSkillIndex(selectedSkillIndex);
        }
    }

    private void SelectFirstReadySkill()
    {
        for (int i = 0; i < 3; i++)
        {
            if (remainingCooldowns[i] <= 0)
            {
                SetSkillIndex(i);
                return;
            }
        }
    }

    private void CancelCommonHeadSelection()
    {
        if (commonHeadUseController == null)
        {
            commonHeadUseController = GetComponent<CommonHeadUseController>();
        }

        commonHeadUseController?.CancelSelectionAndRestoreUniqueHead();
    }

    private void ReadSelectionInput()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame) SetSkillIndex(0);
        if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame) SetSkillIndex(1);
        if (keyboard.digit3Key.wasPressedThisFrame || keyboard.numpad3Key.wasPressedThisFrame) SetSkillIndex(2);
        if (keyboard.f1Key.wasPressedThisFrame) SetCharacterKind(ObjectHeadCharacterKind.Bulb);
        if (keyboard.f2Key.wasPressedThisFrame) SetCharacterKind(ObjectHeadCharacterKind.Seed);
        if (keyboard.f3Key.wasPressedThisFrame) SetCharacterKind(ObjectHeadCharacterKind.Bomb);
#else
        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) SetSkillIndex(0);
        if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) SetSkillIndex(1);
        if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) SetSkillIndex(2);
        if (Input.GetKeyDown(KeyCode.F1)) SetCharacterKind(ObjectHeadCharacterKind.Bulb);
        if (Input.GetKeyDown(KeyCode.F2)) SetCharacterKind(ObjectHeadCharacterKind.Seed);
        if (Input.GetKeyDown(KeyCode.F3)) SetCharacterKind(ObjectHeadCharacterKind.Bomb);
#endif
    }

    private static TurnManager FindTurnManager()
    {
#if UNITY_6000_0_OR_NEWER || UNITY_2023_1_OR_NEWER
        return Object.FindAnyObjectByType<TurnManager>();
#else
        return Object.FindObjectOfType<TurnManager>();
#endif
    }
}

public static class TerrainGrowthSeedUtility
{
    public static int Build(
        CharacterCombat owner,
        TurnManager turnManager,
        int skillId,
        int commonHeadTypeId,
        Vector2 worldPosition)
    {
        ObjectHeadTeamMember member = owner != null
            ? owner.GetComponent<ObjectHeadTeamMember>()
            : null;
        int playerIndex = member != null ? member.PlayerIndex : 0;
        int slotIndex = member != null ? member.TeamSlotIndex : 0;
        int quantizedX = Mathf.RoundToInt(worldPosition.x * 16f);
        int quantizedY = Mathf.RoundToInt(worldPosition.y * 16f);

        unchecked
        {
            int hash = 17;
            hash = hash * 31 + ObjectHeadMatchBootstrap.CurrentMatchSeed;
            hash = hash * 31 + (turnManager != null ? turnManager.TurnSerial : 0);
            hash = hash * 31 + playerIndex;
            hash = hash * 31 + slotIndex;
            hash = hash * 31 + skillId;
            hash = hash * 31 + commonHeadTypeId;
            hash = hash * 31 + quantizedX;
            hash = hash * 31 + quantizedY;
            return hash;
        }
    }
}
