using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public enum TurnPhase
{
    Aiming,
    ProjectileFlying,
    PostImpactDelay,
    Resolving,
    WaitingManualEnd,
    MatchOver
}

public class TurnManager : MonoBehaviour
{
    [SerializeField] private TurnCharacterController[] characters = new TurnCharacterController[0];
    [SerializeField, Min(0)] private int startingIndex;
    [SerializeField] private bool allowManualTurnEnd = true;
    [SerializeField, Min(5f)] private float turnDurationSeconds = 30f;

    private int currentTurnIndex = -1;
    private bool isMatchOver;
    private int winningPlayerIndex = -1;
    private float remainingTurnSeconds;
    private int turnSerial;
    private bool hasStartedAnyTurn;

    public event Action<TurnCharacterController> TurnStarted;
    public event Action<TurnPhase> TurnPhaseChanged;
    public event Action<int> MatchEnded;

    public TurnCharacterController CurrentCharacter { get; private set; }
    public TurnCharacterController[] Characters => characters;
    public int CurrentTurnIndex => currentTurnIndex;
    public TurnPhase CurrentPhase { get; private set; } = TurnPhase.Aiming;
    public int TurnSerial => turnSerial;
    public bool IsMatchOver => isMatchOver;
    public int WinningPlayerIndex => winningPlayerIndex;
    public float TurnDurationSeconds => turnDurationSeconds;
    public float RemainingTurnSeconds => remainingTurnSeconds;
    public float TurnTime01 => turnDurationSeconds > 0f ? Mathf.Clamp01(remainingTurnSeconds / turnDurationSeconds) : 0f;
    public bool IsActionPending =>
        CurrentPhase == TurnPhase.ProjectileFlying ||
        CurrentPhase == TurnPhase.PostImpactDelay ||
        CurrentPhase == TurnPhase.Resolving;

    public int CurrentPlayerIndex
    {
        get
        {
            if (CurrentCharacter == null)
            {
                return -1;
            }

            ObjectHeadTeamMember member = CurrentCharacter.GetComponent<ObjectHeadTeamMember>();
            return member != null ? member.PlayerIndex : currentTurnIndex + 1;
        }
    }

    private void Awake()
    {
        if (characters == null || characters.Length == 0)
        {
            RefreshCharactersFromScene();
        }
    }

    private void Start()
    {
        if (characters == null || characters.Length == 0)
        {
            Debug.LogWarning($"{nameof(TurnManager)} has no turn characters.");
            return;
        }

        SubscribeCharacterDeaths();

        if (CurrentCharacter != null)
        {
            return;
        }

        currentTurnIndex = Mathf.Clamp(startingIndex, 0, characters.Length - 1);
        if (!CanTakeTurn(characters[currentTurnIndex]))
        {
            currentTurnIndex = FindNextTurnIndex(currentTurnIndex - 1);
        }

        ApplyCurrentTurn();
    }

    private void Update()
    {
        if (isMatchOver)
        {
            return;
        }

        TickTurnTimer();

        if (allowManualTurnEnd && WasEndTurnPressed())
        {
            EndTurn();
        }
    }

    public bool CanCharacterMove(TurnCharacterController character)
    {
        if (character == null || character != CurrentCharacter || isMatchOver)
        {
            return false;
        }

        return CurrentPhase == TurnPhase.Aiming ||
               CurrentPhase == TurnPhase.PostImpactDelay ||
               CurrentPhase == TurnPhase.WaitingManualEnd;
    }

    public bool CanCharacterFire(TurnCharacterController character)
    {
        return character != null &&
               character == CurrentCharacter &&
               !isMatchOver &&
               CurrentPhase == TurnPhase.Aiming;
    }

    public bool TryBeginAction(TurnCharacterController character)
    {
        if (!CanCharacterFire(character))
        {
            return false;
        }

        SetPhase(TurnPhase.ProjectileFlying);
        return true;
    }

    public void NotifyPostImpactDelay()
    {
        if (!isMatchOver && CurrentCharacter != null)
        {
            SetPhase(TurnPhase.PostImpactDelay);
        }
    }

    public void NotifyResolving()
    {
        if (!isMatchOver && CurrentCharacter != null)
        {
            SetPhase(TurnPhase.Resolving);
        }
    }

    public void NotifyActionResolved(bool endTurnAutomatically = true)
    {
        if (isMatchOver || CurrentCharacter == null)
        {
            return;
        }

        SetPhase(TurnPhase.WaitingManualEnd);
        if (endTurnAutomatically)
        {
            AdvanceTurn();
        }
    }

    public void EndCurrentTurn()
    {
        EndTurn();
    }

    public void EndTurn()
    {
        if (isMatchOver || IsActionPending)
        {
            return;
        }

        AdvanceTurn();
    }

    public void RefreshCharactersFromScene()
    {
#if UNITY_6000_0_OR_NEWER || UNITY_2023_1_OR_NEWER
        characters = FindObjectsByType<TurnCharacterController>(FindObjectsSortMode.None);
#else
        characters = FindObjectsOfType<TurnCharacterController>();
#endif
        Array.Sort(characters, CompareByTeamThenSlotThenX);
        SubscribeCharacterDeaths();
    }

    public void SetCharacters(TurnCharacterController[] turnCharacters)
    {
        UnsubscribeCharacterDeaths();
        characters = turnCharacters ?? new TurnCharacterController[0];
        isMatchOver = false;
        winningPlayerIndex = -1;
        currentTurnIndex = characters.Length == 0 ? -1 : Mathf.Clamp(startingIndex, 0, characters.Length - 1);
        turnSerial = 0;
        hasStartedAnyTurn = false;
        SubscribeCharacterDeaths();
        ApplyCurrentTurn();
    }

    public void NotifyCharacterDied(CharacterCombat combat)
    {
        if (isMatchOver || combat == null)
        {
            return;
        }

        if (TryResolveVictory())
        {
            return;
        }

        if (CurrentCharacter != null && combat.gameObject == CurrentCharacter.gameObject)
        {
            AdvanceTurn();
        }
    }

    private void TickTurnTimer()
    {
        if (CurrentCharacter == null || CurrentPhase != TurnPhase.Aiming)
        {
            return;
        }

        remainingTurnSeconds = Mathf.Max(0f, remainingTurnSeconds - Time.deltaTime);
        if (remainingTurnSeconds <= 0f)
        {
            Debug.Log("Turn timer expired.");
            AdvanceTurn();
        }
    }

    private void AdvanceTurn()
    {
        if (isMatchOver)
        {
            return;
        }

        if (characters == null || characters.Length == 0)
        {
            RefreshCharactersFromScene();
        }

        if (characters == null || characters.Length == 0 || TryResolveVictory())
        {
            return;
        }

        int nextIndex = FindNextTurnIndex(currentTurnIndex);
        if (nextIndex < 0)
        {
            ClearCurrentTurn();
            Debug.LogWarning($"{nameof(TurnManager)} could not find an available character.");
            return;
        }

        currentTurnIndex = nextIndex;
        ApplyCurrentTurn();
    }

    private void ApplyCurrentTurn()
    {
        if (characters == null || characters.Length == 0 || currentTurnIndex < 0 || isMatchOver)
        {
            ClearCurrentTurn();
            return;
        }

        currentTurnIndex = NormalizeIndex(currentTurnIndex);
        CurrentCharacter = characters[currentTurnIndex];

        if (!CanTakeTurn(CurrentCharacter))
        {
            int next = FindNextTurnIndex(currentTurnIndex);
            if (next < 0)
            {
                ClearCurrentTurn();
                return;
            }

            currentTurnIndex = next;
            CurrentCharacter = characters[currentTurnIndex];
        }

        if (hasStartedAnyTurn)
        {
            AdvanceHazardTurns();
        }

        hasStartedAnyTurn = true;
        turnSerial++;
        remainingTurnSeconds = turnDurationSeconds;
        SetPhase(TurnPhase.Aiming);

        DemoSkillSelector selector = CurrentCharacter != null ? CurrentCharacter.GetComponent<DemoSkillSelector>() : null;
        selector?.NotifyTurnStarted();
        TurnStarted?.Invoke(CurrentCharacter);

        if (CurrentCharacter != null)
        {
            ObjectHeadTeamMember member = CurrentCharacter.GetComponent<ObjectHeadTeamMember>();
            string label = member != null ? member.DisplayName : CurrentCharacter.name;
            Debug.Log($"Turn {turnSerial}: {label}");
        }
    }

    private void SetPhase(TurnPhase phase)
    {
        CurrentPhase = phase;

        if (characters != null)
        {
            for (int i = 0; i < characters.Length; i++)
            {
                TurnCharacterController character = characters[i];
                if (character == null)
                {
                    continue;
                }

                bool canMove = character == CurrentCharacter && CanCharacterMove(character);
                character.SetControlEnabled(canMove);

                if (!canMove)
                {
                    character.StopHorizontalMovement();
                }
            }
        }

        TurnPhaseChanged?.Invoke(CurrentPhase);
    }

    private void AdvanceHazardTurns()
    {
#if UNITY_6000_0_OR_NEWER || UNITY_2023_1_OR_NEWER
        HazardZone[] zones = FindObjectsByType<HazardZone>(FindObjectsSortMode.None);
#else
        HazardZone[] zones = FindObjectsOfType<HazardZone>();
#endif
        for (int i = 0; i < zones.Length; i++)
        {
            zones[i]?.NotifyTurnAdvanced();
        }
    }

    private bool TryResolveVictory()
    {
        HashSet<int> alivePlayers = new HashSet<int>();
        bool hasTeamInfo = false;

        if (characters == null)
        {
            return false;
        }

        for (int i = 0; i < characters.Length; i++)
        {
            TurnCharacterController character = characters[i];
            if (!CanTakeTurn(character))
            {
                continue;
            }

            ObjectHeadTeamMember member = character.GetComponent<ObjectHeadTeamMember>();
            if (member != null)
            {
                hasTeamInfo = true;
                alivePlayers.Add(member.PlayerIndex);
            }
            else
            {
                alivePlayers.Add(i + 1);
            }
        }

        if (!hasTeamInfo || alivePlayers.Count > 1)
        {
            return false;
        }

        if (alivePlayers.Count == 1)
        {
            foreach (int player in alivePlayers)
            {
                EndMatch(player);
                return true;
            }
        }

        return false;
    }

    private void EndMatch(int playerIndex)
    {
        isMatchOver = true;
        winningPlayerIndex = playerIndex;
        ClearCurrentTurn();
        CurrentPhase = TurnPhase.MatchOver;
        TurnPhaseChanged?.Invoke(CurrentPhase);
        Debug.Log($"Player {playerIndex} wins.");
        MatchEnded?.Invoke(playerIndex);
    }

    private void ClearCurrentTurn()
    {
        CurrentCharacter = null;
        currentTurnIndex = -1;
        remainingTurnSeconds = 0f;

        if (characters == null)
        {
            return;
        }

        foreach (TurnCharacterController character in characters)
        {
            if (character != null)
            {
                character.SetControlEnabled(false);
                character.StopHorizontalMovement();
            }
        }
    }

    private int FindNextTurnIndex(int fromIndex)
    {
        int count = characters.Length;
        for (int offset = 1; offset <= count; offset++)
        {
            int candidateIndex = NormalizeIndex(fromIndex + offset);
            if (CanTakeTurn(characters[candidateIndex]))
            {
                return candidateIndex;
            }
        }

        return -1;
    }

    private int NormalizeIndex(int index)
    {
        if (characters == null || characters.Length == 0)
        {
            return -1;
        }

        int count = characters.Length;
        return ((index % count) + count) % count;
    }

    private bool CanTakeTurn(TurnCharacterController character)
    {
        if (character == null || !character.IsTurnAvailable)
        {
            return false;
        }

        CharacterCombat combat = character.GetComponent<CharacterCombat>();
        return combat == null || !combat.IsDead;
    }

    private void SubscribeCharacterDeaths()
    {
        if (characters == null)
        {
            return;
        }

        for (int i = 0; i < characters.Length; i++)
        {
            CharacterCombat combat = characters[i] != null ? characters[i].GetComponent<CharacterCombat>() : null;
            if (combat != null)
            {
                combat.Died -= NotifyCharacterDied;
                combat.Died += NotifyCharacterDied;
            }
        }
    }

    private void UnsubscribeCharacterDeaths()
    {
        if (characters == null)
        {
            return;
        }

        for (int i = 0; i < characters.Length; i++)
        {
            CharacterCombat combat = characters[i] != null ? characters[i].GetComponent<CharacterCombat>() : null;
            if (combat != null)
            {
                combat.Died -= NotifyCharacterDied;
            }
        }
    }

    private int CompareByTeamThenSlotThenX(TurnCharacterController left, TurnCharacterController right)
    {
        if (left == right) return 0;
        if (left == null) return 1;
        if (right == null) return -1;

        ObjectHeadTeamMember leftMember = left.GetComponent<ObjectHeadTeamMember>();
        ObjectHeadTeamMember rightMember = right.GetComponent<ObjectHeadTeamMember>();
        int leftSlot = leftMember != null ? leftMember.TeamSlotIndex : 999;
        int rightSlot = rightMember != null ? rightMember.TeamSlotIndex : 999;
        int slotCompare = leftSlot.CompareTo(rightSlot);
        if (slotCompare != 0) return slotCompare;

        int leftPlayer = leftMember != null ? leftMember.PlayerIndex : 999;
        int rightPlayer = rightMember != null ? rightMember.PlayerIndex : 999;
        int playerCompare = leftPlayer.CompareTo(rightPlayer);
        if (playerCompare != 0) return playerCompare;

        return left.transform.position.x.CompareTo(right.transform.position.x);
    }

    private bool WasEndTurnPressed()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.tabKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Tab);
#endif
    }
}
