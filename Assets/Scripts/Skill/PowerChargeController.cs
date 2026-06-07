using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public class PowerChargeController : MonoBehaviour
{
    [SerializeField, Min(0.05f)] private float chargeSeconds = 1.2f;
    [SerializeField, Range(0f, 1f)] private float minimumFirePower = 0.1f;

    private TurnCharacterController turnCharacter;
    private AimController aimController;
    private TurnManager turnManager;
    private bool isCharging;
    private bool hasReleasedPower;
    private float currentPower;
    private float releasedPower;

    public bool IsCharging => isCharging;
    public float CurrentPower => currentPower;

    private void Awake()
    {
        turnCharacter = GetComponent<TurnCharacterController>();
        aimController = GetComponent<AimController>();
        turnManager = FindTurnManager();
    }

    private void OnDisable()
    {
        ResetCharge();
        hasReleasedPower = false;
    }

    private void Update()
    {
        if (turnManager == null)
        {
            turnManager = FindTurnManager();
        }

        if (turnCharacter == null ||
            !turnCharacter.HasControl ||
            turnManager == null ||
            !turnManager.CanCharacterFire(turnCharacter))
        {
            ResetCharge();
            return;
        }

        ReadChargeInput(out bool pressed, out bool held, out bool released);

        if (pressed)
        {
            isCharging = true;
            currentPower = 0f;
        }

        if (isCharging && held)
        {
            currentPower = Mathf.Clamp01(currentPower + Time.deltaTime / chargeSeconds);
            aimController?.SetChargePower(currentPower);

            if (currentPower >= 1f)
            {
                ReleasePower(1f);
                return;
            }
        }

        if (isCharging && released)
        {
            ReleasePower(Mathf.Max(currentPower, minimumFirePower));
        }
    }

    private TurnManager FindTurnManager()
    {
#if UNITY_6000_0_OR_NEWER || UNITY_2023_1_OR_NEWER
        return Object.FindAnyObjectByType<TurnManager>();
#else
        return Object.FindObjectOfType<TurnManager>();
#endif
    }

    public bool ConsumeReleasedPower(out float power)
    {
        power = releasedPower;

        if (!hasReleasedPower)
        {
            return false;
        }

        hasReleasedPower = false;
        releasedPower = 0f;
        return true;
    }

    private void ReleasePower(float power)
    {
        releasedPower = Mathf.Clamp01(power);
        hasReleasedPower = true;
        ResetCharge();
    }

    private void ResetCharge()
    {
        isCharging = false;
        currentPower = 0f;
        aimController?.SetChargePower(0f);
    }

    private void ReadChargeInput(out bool pressed, out bool held, out bool released)
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        pressed = keyboard != null && keyboard.spaceKey.wasPressedThisFrame;
        held = keyboard != null && keyboard.spaceKey.isPressed;
        released = keyboard != null && keyboard.spaceKey.wasReleasedThisFrame;
#else
        pressed = Input.GetKeyDown(KeyCode.Space);
        held = Input.GetKey(KeyCode.Space);
        released = Input.GetKeyUp(KeyCode.Space);
#endif
    }
}
