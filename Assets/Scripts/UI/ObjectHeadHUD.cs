using UnityEngine;

[DisallowMultipleComponent]
public class ObjectHeadHUD : MonoBehaviour
{
    [SerializeField] private TurnManager turnManager;
    [SerializeField] private bool showHelp = true;

    private readonly string[] skillNames = { "1 Basic", "2 Tactical", "3 Power" };
    private GUIStyle panelStyle;
    private GUIStyle labelStyle;
    private GUIStyle titleStyle;
    private GUIStyle warningStyle;
    private Texture2D whiteTexture;

    private void Start()
    {
        if (turnManager == null)
        {
            turnManager = FindAny<TurnManager>();
        }
    }

    private void OnGUI()
    {
        EnsureStyles();

        GUILayout.BeginArea(new Rect(14f, 14f, 380f, Screen.height - 28f), panelStyle);
        DrawTurnInfo();
        GUILayout.Space(8f);
        DrawSkillInfo();
        GUILayout.Space(8f);
        DrawTeamInfo();
        if (showHelp)
        {
            GUILayout.Space(8f);
            DrawHelp();
        }
        GUILayout.EndArea();

        DrawMatchResult();
    }

    private void DrawTurnInfo()
    {
        GUILayout.Label("Object Head Battle", titleStyle);

        if (turnManager == null)
        {
            GUILayout.Label("TurnManager not found", warningStyle);
            return;
        }

        TurnCharacterController current = turnManager.CurrentCharacter;
        if (current == null)
        {
            GUILayout.Label("Current turn: none", warningStyle);
            return;
        }

        ObjectHeadTeamMember member = current.GetComponent<ObjectHeadTeamMember>();
        CharacterCombat combat = current.GetComponent<CharacterCombat>();
        DemoSkillSelector selector = current.GetComponent<DemoSkillSelector>();
        PowerChargeController power = current.GetComponent<PowerChargeController>();

        string playerText = member != null ? $"P{member.PlayerIndex} / Slot {member.TeamSlotIndex}" : current.name;
        string kindText = selector != null ? selector.CharacterKind.ToString() : "Unknown";
        GUILayout.Label($"Turn: {playerText}  {kindText}", labelStyle);
        GUILayout.Label($"Time: {Mathf.CeilToInt(turnManager.RemainingTurnSeconds)}s", turnManager.RemainingTurnSeconds <= 5f ? warningStyle : labelStyle);
        DrawHorizontalMeter(turnManager.TurnTime01, new Color(0.25f, 0.85f, 1f, 0.95f), 348f, 8f);

        if (combat != null)
        {
            GUILayout.Label($"HP: {combat.CurrentHp}/{combat.MaxHp}", labelStyle);
        }

        if (power != null)
        {
            GUILayout.Label($"Power: {(power.CurrentPower * 100f):0}%", labelStyle);
            DrawHorizontalMeter(power.CurrentPower, new Color(1f, 0.85f, 0.2f, 0.95f), 348f, 8f);
        }
    }

    private void DrawSkillInfo()
    {
        if (turnManager == null || turnManager.CurrentCharacter == null)
        {
            return;
        }

        DemoSkillSelector selector = turnManager.CurrentCharacter.GetComponent<DemoSkillSelector>();
        if (selector == null)
        {
            return;
        }

        GUILayout.Label("Skills", titleStyle);
        for (int i = 0; i < 3; i++)
        {
            int cooldown = selector.GetRemainingCooldown(i);
            string selected = selector.SelectedSkillIndex == i ? "> " : "  ";
            string state = cooldown > 0 ? $"CD {cooldown}" : "Ready";
            GUILayout.Label($"{selected}{skillNames[i]} - {state}", cooldown > 0 ? warningStyle : labelStyle);
        }
    }

    private void DrawTeamInfo()
    {
        if (turnManager == null)
        {
            return;
        }

        GUILayout.Label("Teams", titleStyle);
        TurnCharacterController[] characters = turnManager.Characters;
        for (int i = 0; i < characters.Length; i++)
        {
            if (characters[i] == null)
            {
                continue;
            }

            ObjectHeadTeamMember member = characters[i].GetComponent<ObjectHeadTeamMember>();
            CharacterCombat combat = characters[i].GetComponent<CharacterCombat>();
            string nameText = member != null ? member.DisplayName : characters[i].name;
            string hpText = combat != null ? $"{combat.CurrentHp}/{combat.MaxHp}" : "?";
            GUILayout.Label($"{nameText}: {hpText}", combat != null && combat.IsDead ? warningStyle : labelStyle);
        }
    }

    private void DrawHelp()
    {
        GUILayout.Label("Controls", titleStyle);
        GUILayout.Label("A/D move, W jump, mouse aim", labelStyle);
        GUILayout.Label("Space charge/fire, 1/2/3 skill", labelStyle);
        GUILayout.Label("Tab end turn, I/J/K/L or mouse drag camera", labelStyle);
        GUILayout.Label("Mouse wheel or +/- zoom, Home reset, O overview", labelStyle);
    }

    private void DrawHorizontalMeter(float value01, Color fillColor, float width, float height)
    {
        Rect rect = GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));
        Color previous = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.45f);
        GUI.DrawTexture(rect, whiteTexture);
        GUI.color = fillColor;
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(value01), rect.height), whiteTexture);
        GUI.color = previous;
    }

    private void DrawMatchResult()
    {
        if (turnManager == null || !turnManager.IsMatchOver)
        {
            return;
        }

        Rect box = new Rect(Screen.width * 0.5f - 180f, 28f, 360f, 72f);
        GUI.Box(box, GUIContent.none, panelStyle);
        GUI.Label(new Rect(box.x + 18f, box.y + 15f, box.width - 36f, 42f), $"P{turnManager.WinningPlayerIndex} Wins", titleStyle);
    }

    private void EnsureStyles()
    {
        if (panelStyle != null)
        {
            return;
        }

        whiteTexture = Texture2D.whiteTexture;
        panelStyle = new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(12, 12, 10, 10),
            normal = { textColor = Color.white }
        };

        labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            normal = { textColor = Color.white }
        };

        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };

        warningStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            normal = { textColor = new Color(1f, 0.75f, 0.25f, 1f) }
        };
    }

    private static T FindAny<T>() where T : Object
    {
#if UNITY_6000_0_OR_NEWER || UNITY_2023_1_OR_NEWER
        return Object.FindAnyObjectByType<T>();
#else
        return Object.FindObjectOfType<T>();
#endif
    }
}

