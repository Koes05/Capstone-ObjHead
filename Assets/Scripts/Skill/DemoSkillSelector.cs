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
            blinkSpriteB = null
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

    private readonly int[] remainingCooldowns = new int[3];
    private CharacterVisual characterVisual;
    private TurnCharacterController turnCharacter;

    public ObjectHeadCharacterKind CharacterKind => characterKind;
    public int SelectedSkillIndex => selectedSkillIndex;

    private void Awake()
    {
        characterVisual = GetComponent<CharacterVisual>();
        turnCharacter = GetComponent<TurnCharacterController>();
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
        characterKind = kind;
        ApplySelection();
    }

    public void SetSkillIndex(int index)
    {
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
            settings.maxDamage = 0;
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
            return;
        }

        if (selectedSkillIndex == 1)
        {
            settings.effectType = SkillEffectType.CreateTerrainBridge;
            settings.maxDamage = 3;
            settings.explosionRadiusWorld = 0.38f;
            settings.bridgeLengthWorld = 4.8f;
            settings.bridgeThicknessPx = 8;
            return;
        }

        settings.effectType = SkillEffectType.CreateHazardZone;
        settings.maxDamage = 0;
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
        settings.chainDelaySeconds = 0.11f;
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
