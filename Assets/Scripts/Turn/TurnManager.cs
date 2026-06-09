using System;
using System.Collections;
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
    [SerializeField, Min(0.1f)] private float residualMovementSeconds = 5f;
    [SerializeField, Min(0f)] private float damageSettlementSeconds = 1f;

    private int currentTurnIndex = -1;
    private bool isMatchOver;
    private int winningPlayerIndex = -1;
    private float remainingTurnSeconds;
    private int turnSerial;
    private int roundSerial;
    private bool hasStartedAnyTurn;
    private bool actionUsedThisTurn;
    private bool turnEndRequested;
    private bool victoryCheckPending;
    private bool residualTimeActive;
    private bool applyingResidualDamage;
    private bool settlementTimeActive;
    private float remainingResidualSeconds;
    private float remainingSettlementSeconds;
    private Coroutine damageSettlementRoutine;

    public event Action<TurnCharacterController> TurnStarted;
    public event Action<TurnCharacterController> TurnEnded;
    public event Action<int> RoundStarted;
    public event Action<TurnPhase> TurnPhaseChanged;
    public event Action<int> MatchEnded;

    public TurnCharacterController CurrentCharacter { get; private set; }
    public TurnCharacterController[] Characters => characters;
    public int CurrentTurnIndex => currentTurnIndex;
    public TurnPhase CurrentPhase { get; private set; } = TurnPhase.Aiming;
    public int TurnSerial => turnSerial;
    public int RoundSerial => roundSerial;
    public bool ActionUsedThisTurn => actionUsedThisTurn;
    public bool TurnEndRequested => turnEndRequested;
    public bool IsMatchOver => isMatchOver;
    public int WinningPlayerIndex => winningPlayerIndex;
    public float TurnDurationSeconds => turnDurationSeconds;
    public float RemainingTurnSeconds => remainingTurnSeconds;
    public float TurnTime01 => turnDurationSeconds > 0f ? Mathf.Clamp01(remainingTurnSeconds / turnDurationSeconds) : 0f;
    public float ResidualMovementSeconds => residualMovementSeconds;
    public float RemainingResidualSeconds => remainingResidualSeconds;
    public float ResidualTime01 => residualMovementSeconds > 0f ? Mathf.Clamp01(remainingResidualSeconds / residualMovementSeconds) : 0f;
    public bool IsResidualTimeActive => residualTimeActive;
    public float DamageSettlementSeconds => damageSettlementSeconds;
    public float RemainingSettlementSeconds => remainingSettlementSeconds;
    public float SettlementTime01 => damageSettlementSeconds > 0f ? Mathf.Clamp01(remainingSettlementSeconds / damageSettlementSeconds) : 0f;
    public bool IsSettlementTimeActive => settlementTimeActive;
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

        roundSerial = 1;
        ApplyCurrentTurn(true);
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
        if (character == null || character != CurrentCharacter || isMatchOver || settlementTimeActive)
        {
            return false;
        }

        if (residualTimeActive)
        {
            return true;
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
               !actionUsedThisTurn &&
               !residualTimeActive &&
               CurrentPhase == TurnPhase.Aiming;
    }

    public bool TryBeginAction(TurnCharacterController character)
    {
        if (!CanCharacterFire(character))
        {
            return false;
        }

        actionUsedThisTurn = true;
        StartResidualTime("Action used.");
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

    public void NotifyActionResolved(bool unusedLegacyAutoEnd = false)
    {
        if (isMatchOver)
        {
            return;
        }

        if (victoryCheckPending && TryResolveVictory())
        {
            return;
        }

        if (CurrentCharacter == null)
        {
            return;
        }

        if (settlementTimeActive)
        {
            return;
        }

        SetPhase(TurnPhase.WaitingManualEnd);
        CharacterCombat combat = CurrentCharacter.GetComponent<CharacterCombat>();
        if (turnEndRequested || (combat != null && combat.IsDead))
        {
            AdvanceTurn();
            return;
        }

        if (!residualTimeActive && remainingTurnSeconds <= 0f)
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
        if (isMatchOver || CurrentCharacter == null)
        {
            return;
        }

        if (residualTimeActive || settlementTimeActive)
        {
            turnEndRequested = true;
            return;
        }

        if (IsActionPending)
        {
            turnEndRequested = true;
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
        victoryCheckPending = false;
        currentTurnIndex = characters.Length == 0 ? -1 : Mathf.Clamp(startingIndex, 0, characters.Length - 1);
        turnSerial = 0;
        roundSerial = characters.Length > 0 ? 1 : 0;
        hasStartedAnyTurn = false;
        SubscribeCharacterDeaths();
        ApplyCurrentTurn(characters.Length > 0);
    }

    public void NotifyCharacterDied(CharacterCombat combat)
    {
        if (isMatchOver || combat == null)
        {
            return;
        }

        if (applyingResidualDamage)
        {
            victoryCheckPending = true;
            if (CurrentCharacter != null && combat.gameObject == CurrentCharacter.gameObject)
            {
                turnEndRequested = true;
            }
            return;
        }

        if (IsActionPending)
        {
            victoryCheckPending = true;
            if (CurrentCharacter != null && combat.gameObject == CurrentCharacter.gameObject)
            {
                turnEndRequested = true;
            }
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
        if (CurrentCharacter == null || CurrentPhase == TurnPhase.MatchOver)
        {
            return;
        }

        if (settlementTimeActive)
        {
            return;
        }

        if (residualTimeActive)
        {
            TickResidualTimer();
            return;
        }

        if (CurrentPhase != TurnPhase.Aiming)
        {
            return;
        }

        remainingTurnSeconds = Mathf.Max(0f, remainingTurnSeconds - Time.deltaTime);
        if (remainingTurnSeconds > 0f)
        {
            return;
        }

        if (IsActionPending)
        {
            turnEndRequested = true;
            return;
        }

        Debug.Log("Turn timer expired.");
        StartResidualTime("Turn timer expired.");
    }

    private void TickResidualTimer()
    {
        remainingResidualSeconds = Mathf.Max(0f, remainingResidualSeconds - Time.deltaTime);
        if (remainingResidualSeconds > 0f)
        {
            return;
        }

        residualTimeActive = false;
        remainingResidualSeconds = 0f;
        Debug.Log("Residual movement timer expired.");
        bool appliedPendingDamage = FlushPendingResidualDamage();

        if (isMatchOver || CurrentCharacter == null)
        {
            return;
        }

        if (appliedPendingDamage && damageSettlementSeconds > 0f)
        {
            BeginDamageSettlementTime();
            return;
        }

        CompletePostResidualTransition();
    }

    private void CompletePostResidualTransition()
    {
        if (isMatchOver || CurrentCharacter == null)
        {
            return;
        }

        if (victoryCheckPending && TryResolveVictory())
        {
            return;
        }

        if (IsActionPending)
        {
            turnEndRequested = true;
            ApplyCharacterControlState();
            return;
        }

        CharacterCombat currentCombat = CurrentCharacter.GetComponent<CharacterCombat>();
        if (turnEndRequested || (currentCombat != null && currentCombat.IsDead))
        {
            AdvanceTurn();
            return;
        }

        AdvanceTurn();
    }

    private void StartResidualTime(string reason)
    {
        if (residualTimeActive || settlementTimeActive || isMatchOver || CurrentCharacter == null)
        {
            return;
        }

        remainingTurnSeconds = 0f;
        remainingResidualSeconds = residualMovementSeconds;
        residualTimeActive = true;
        Debug.Log($"{reason} Residual movement time started: {residualMovementSeconds:0.#}s.");
        ApplyCharacterControlState();
    }

    private bool FlushPendingResidualDamage()
    {
        if (characters == null)
        {
            return false;
        }

        bool appliedAnyDamage = false;
        applyingResidualDamage = true;
        try
        {
            for (int i = 0; i < characters.Length; i++)
            {
                CharacterCombat combat = characters[i] != null
                    ? characters[i].GetComponent<CharacterCombat>()
                    : null;
                if (combat != null && combat.ApplyPendingDamage() > 0)
                {
                    appliedAnyDamage = true;
                }
            }
        }
        finally
        {
            applyingResidualDamage = false;
        }

        return appliedAnyDamage;
    }

    private void BeginDamageSettlementTime()
    {
        if (damageSettlementRoutine != null)
        {
            StopCoroutine(damageSettlementRoutine);
        }

        settlementTimeActive = true;
        remainingSettlementSeconds = damageSettlementSeconds;
        ApplyCharacterControlState();
        damageSettlementRoutine = StartCoroutine(DamageSettlementRoutine());
    }

    private IEnumerator DamageSettlementRoutine()
    {
        remainingSettlementSeconds = Mathf.Max(0f, damageSettlementSeconds);
        while (remainingSettlementSeconds > 0f)
        {
            remainingSettlementSeconds = Mathf.Max(0f, remainingSettlementSeconds - Time.deltaTime);
            yield return null;
        }

        settlementTimeActive = false;
        remainingSettlementSeconds = 0f;
        damageSettlementRoutine = null;
        CompletePostResidualTransition();
    }

    private void AdvanceTurn()
    {
        if (isMatchOver)
        {
            return;
        }

        TurnCharacterController endingCharacter = CurrentCharacter;
        if (endingCharacter != null)
        {
            TurnEnded?.Invoke(endingCharacter);
            endingCharacter.GetComponent<CommonHeadUseController>()?.CancelSelectionAndRestoreUniqueHead();
            endingCharacter.ResetTurnStatus();
        }

        if (characters == null || characters.Length == 0)
        {
            RefreshCharactersFromScene();
        }

        if (characters == null || characters.Length == 0 || TryResolveVictory())
        {
            return;
        }

        int previousIndex = currentTurnIndex;
        int nextIndex = FindNextTurnIndex(previousIndex);
        if (nextIndex < 0)
        {
            ClearCurrentTurn();
            Debug.LogWarning($"{nameof(TurnManager)} could not find an available character.");
            return;
        }

        bool wrappedRound = hasStartedAnyTurn && nextIndex <= previousIndex;
        if (wrappedRound)
        {
            roundSerial++;
        }

        currentTurnIndex = nextIndex;
        ApplyCurrentTurn(wrappedRound);
    }

    private void ApplyCurrentTurn(bool announceRound)
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

        for (int i = 0; i < characters.Length; i++)
        {
            characters[i]?.ResetTurnStatus();
        }

        hasStartedAnyTurn = true;
        turnSerial++;
        actionUsedThisTurn = false;
        turnEndRequested = false;
        victoryCheckPending = false;
        residualTimeActive = false;
        StopDamageSettlementRoutine();
        remainingTurnSeconds = turnDurationSeconds;
        remainingResidualSeconds = 0f;
        SetPhase(TurnPhase.Aiming);

        if (announceRound)
        {
            RoundStarted?.Invoke(roundSerial);
            Debug.Log($"Round {roundSerial} started.");
        }

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
        ApplyCharacterControlState();
        TurnPhaseChanged?.Invoke(CurrentPhase);
    }

    private void ApplyCharacterControlState()
    {
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

        if (!hasTeamInfo || alivePlayers.Count != 1)
        {
            victoryCheckPending = false;
            return false;
        }

        foreach (int player in alivePlayers)
        {
            EndMatch(player);
            return true;
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
        remainingResidualSeconds = 0f;
        residualTimeActive = false;
        StopDamageSettlementRoutine();
        if (characters == null)
        {
            return;
        }

        foreach (TurnCharacterController character in characters)
        {
            if (character != null)
            {
                character.GetComponent<CommonHeadUseController>()?.CancelSelectionAndRestoreUniqueHead();
                character.ResetTurnStatus();
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

    private void StopDamageSettlementRoutine()
    {
        if (damageSettlementRoutine != null)
        {
            StopCoroutine(damageSettlementRoutine);
            damageSettlementRoutine = null;
        }

        settlementTimeActive = false;
        remainingSettlementSeconds = 0f;
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
