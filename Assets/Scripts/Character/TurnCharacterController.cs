using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class TurnCharacterController : MonoBehaviour
{
    private const float ExternalMotionGroundedYThreshold = 0.05f;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float jumpForce = 8f;

    [Header("Gravity")]
    [SerializeField] private float customGravity = 25f;
    [SerializeField] private float minJumpVelocityMultiplier = 0.5f;
    [SerializeField] private float heldJumpGravityMultiplier = 0.4f;
    [SerializeField] private float gravityReturnDuration = 0.37f;
    [SerializeField] private float fallGravityIncreasePerSecond = 0.25f;
    [SerializeField] private float maxGravityMultiplier = 2f;
    [SerializeField] private float maxFallSpeed = 18f;

    [Header("Ground Check")]
    [SerializeField] private float groundCheckDistance = 0.08f;
    [SerializeField] private LayerMask groundLayer = ~0;

    private readonly RaycastHit2D[] groundHits = new RaycastHit2D[4];
    private Rigidbody2D body;
    private Collider2D bodyCollider;
    private CharacterVisual characterVisual;
    private float horizontalInput;
    private bool jumpRequested;
    private bool jumpHeld;
    private bool jumpReleased;
    private bool isJumping;
    private bool isGrounded;
    private bool hasControl;
    private float externalMotionTimer;
    private float fallGravityTimer;
    private float timedMoveSpeedMultiplier = 1f;
    private float timedMoveSpeedMultiplierTimer;
    private float hazardMoveSpeedMultiplier = 1f;

    public bool HasControl => hasControl;
    public bool IsGrounded => isGrounded;
    public bool IsTurnAvailable => isActiveAndEnabled && gameObject.activeInHierarchy;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<Collider2D>();
        characterVisual = GetComponent<CharacterVisual>();
        body.gravityScale = 0f;
    }

    private void OnDisable()
    {
        hasControl = false;
        ResetInput();
        ClearHazardSlow();
    }

    private void Update()
    {
        if (!hasControl)
        {
            ResetInput();
            return;
        }

#if ENABLE_INPUT_SYSTEM
        ReadInputSystemKeyboard();
#else
        ReadLegacyKeyboard();
#endif
    }

    private void FixedUpdate()
    {
        isGrounded = CheckGrounded();
        Vector2 velocity = body.linearVelocity;
        bool preserveExternalMotion = ShouldPreserveExternalMotion(velocity);
        bool canUseInput = hasControl && !preserveExternalMotion;
        UpdateTemporaryMoveSpeedMultiplier();

        if (!preserveExternalMotion)
        {
            if (canUseInput)
            {
                float multiplier = Mathf.Min(timedMoveSpeedMultiplier, hazardMoveSpeedMultiplier);
                velocity.x = horizontalInput * moveSpeed * multiplier;
            }
            else if (isGrounded)
            {
                velocity.x = 0f;
            }
        }

        externalMotionTimer = Mathf.Max(0f, externalMotionTimer - Time.fixedDeltaTime);
        if (isGrounded && velocity.y <= 0f)
        {
            isJumping = false;
            fallGravityTimer = 0f;
        }

        if (canUseInput && jumpRequested && isGrounded)
        {
            velocity.y = jumpForce;
            isJumping = true;
            fallGravityTimer = 0f;
        }

        if (jumpReleased && isJumping && velocity.y > jumpForce * minJumpVelocityMultiplier)
        {
            velocity.y = jumpForce * minJumpVelocityMultiplier;
        }

        bool useHeldJumpGravity = isJumping && jumpHeld && velocity.y > 0f;
        if (isGrounded && velocity.y < 0f)
        {
            velocity.y = 0f;
        }
        else if (!isGrounded || velocity.y > 0f)
        {
            float gravityMultiplier = useHeldJumpGravity ? heldJumpGravityMultiplier : GetFallingGravityMultiplier();
            velocity.y = Mathf.Max(velocity.y - customGravity * gravityMultiplier * Time.fixedDeltaTime, -maxFallSpeed);
            if (!useHeldJumpGravity)
            {
                fallGravityTimer += Time.fixedDeltaTime;
            }
        }

        body.linearVelocity = velocity;
        jumpRequested = false;
        jumpReleased = false;
    }

    public void SetControlEnabled(bool enabled)
    {
        hasControl = enabled && IsTurnAvailable;
        if (!hasControl)
        {
            ResetInput();
        }
    }

    public void PreserveExternalMotion(float seconds)
    {
        externalMotionTimer = Mathf.Max(externalMotionTimer, seconds);
        ResetInput();
    }

    public void ApplyMoveSpeedMultiplier(float multiplier, float seconds)
    {
        timedMoveSpeedMultiplier = Mathf.Min(timedMoveSpeedMultiplier, Mathf.Clamp(multiplier, 0.1f, 1f));
        timedMoveSpeedMultiplierTimer = Mathf.Max(timedMoveSpeedMultiplierTimer, seconds);
    }

    public void SetHazardMoveSpeedMultiplier(float multiplier)
    {
        hazardMoveSpeedMultiplier = Mathf.Clamp(multiplier, 0.1f, 1f);
    }

    public void ClearHazardSlow()
    {
        hazardMoveSpeedMultiplier = 1f;
    }

    public void ResetTurnStatus()
    {
        timedMoveSpeedMultiplier = 1f;
        timedMoveSpeedMultiplierTimer = 0f;
        hazardMoveSpeedMultiplier = 1f;
        externalMotionTimer = 0f;
        ResetInput();
    }

    public void StopHorizontalMovement()
    {
        if (body == null)
        {
            return;
        }

        Vector2 velocity = body.linearVelocity;
        if (ShouldPreserveExternalMotion(velocity))
        {
            return;
        }

        velocity.x = 0f;
        body.linearVelocity = velocity;
    }

    private bool ShouldPreserveExternalMotion(Vector2 velocity)
    {
        return externalMotionTimer > 0f && (!isGrounded || velocity.y > ExternalMotionGroundedYThreshold);
    }

#if ENABLE_INPUT_SYSTEM
    private void ReadInputSystemKeyboard()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            ResetInput();
            return;
        }

        horizontalInput = 0f;
        if (keyboard.aKey.isPressed) horizontalInput -= 1f;
        if (keyboard.dKey.isPressed) horizontalInput += 1f;
        UpdateFacingFromMovement();

        bool wasJumpHeld = jumpHeld;
        jumpHeld = keyboard.wKey.isPressed || keyboard.enterKey.isPressed || keyboard.numpadEnterKey.isPressed;
        if (keyboard.wKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
        {
            jumpRequested = true;
        }

        if (wasJumpHeld && !jumpHeld)
        {
            jumpReleased = true;
        }
    }
#else
    private void ReadLegacyKeyboard()
    {
        horizontalInput = 0f;
        if (Input.GetKey(KeyCode.A)) horizontalInput -= 1f;
        if (Input.GetKey(KeyCode.D)) horizontalInput += 1f;
        UpdateFacingFromMovement();

        bool wasJumpHeld = jumpHeld;
        jumpHeld = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter);
        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            jumpRequested = true;
        }

        if (wasJumpHeld && !jumpHeld)
        {
            jumpReleased = true;
        }
    }
#endif

    private void UpdateFacingFromMovement()
    {
        if (horizontalInput < -0.01f)
        {
            characterVisual?.SetFacingRight(false);
        }
        else if (horizontalInput > 0.01f)
        {
            characterVisual?.SetFacingRight(true);
        }
    }

    private void ResetInput()
    {
        horizontalInput = 0f;
        jumpRequested = false;
        jumpHeld = false;
        jumpReleased = false;
    }

    private float GetFallingGravityMultiplier()
    {
        if (gravityReturnDuration > 0f && fallGravityTimer < gravityReturnDuration)
        {
            return Mathf.Lerp(heldJumpGravityMultiplier, 1f, fallGravityTimer / gravityReturnDuration);
        }

        float accelerationTime = Mathf.Max(0f, fallGravityTimer - gravityReturnDuration);
        return Mathf.Min(1f + accelerationTime * fallGravityIncreasePerSecond, maxGravityMultiplier);
    }

    private void UpdateTemporaryMoveSpeedMultiplier()
    {
        if (timedMoveSpeedMultiplierTimer <= 0f)
        {
            timedMoveSpeedMultiplier = 1f;
            return;
        }

        timedMoveSpeedMultiplierTimer = Mathf.Max(0f, timedMoveSpeedMultiplierTimer - Time.fixedDeltaTime);
        if (timedMoveSpeedMultiplierTimer <= 0f)
        {
            timedMoveSpeedMultiplier = 1f;
        }
    }

    private bool CheckGrounded()
    {
        ContactFilter2D filter = new ContactFilter2D();
        filter.useTriggers = false;
        filter.SetLayerMask(groundLayer);
        filter.useLayerMask = true;
        return bodyCollider != null && bodyCollider.Cast(Vector2.down, filter, groundHits, groundCheckDistance) > 0;
    }
}
