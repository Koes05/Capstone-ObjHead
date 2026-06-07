using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
public class AimController : MonoBehaviour
{
    [Header("Sprites")]
    [SerializeField] private Sprite targetingSprite;
    [SerializeField] private Sprite chargingSprite;

    [Header("Aim")]
    [SerializeField] private Vector2 originOffset = Vector2.zero;
    [SerializeField, Min(0.1f)] private float aimRadius = 2.2f;
    [SerializeField, Min(0.1f)] private float chargingRadius = 1.45f;
    [SerializeField, Min(1f)] private float rotateSpeedDegrees = 120f;
    [SerializeField, Range(-90f, 90f)] private float localAimAngleDegrees;
    [SerializeField] private bool startsFacingRight = true;

    [Header("Visual")]
    [SerializeField, Min(0.01f)] private float targetingScale = 0.18f;
    [SerializeField, Min(0.01f)] private float chargingScale = 0.16f;
    [SerializeField] private float targetingSpriteAngleOffset;
    [SerializeField] private float chargingSpriteAngleOffset = 90f;
    [SerializeField] private int visualSortingOrder = 25;

    private TurnCharacterController turnCharacter;
    private CharacterVisual characterVisual;
    private int facingSign = 1;
    private float chargePower;
    private Transform targetingRoot;
    private Transform targetingImage;
    private SpriteRenderer targetingRenderer;
    private Transform chargingRoot;
    private Transform chargingImage;
    private Transform chargingMaskTransform;
    private SpriteRenderer chargingRenderer;
    private SpriteMask chargingMask;
    private static Sprite maskSprite;
    private static Sprite fallbackTargetingSprite;
    private static Sprite fallbackChargingSprite;

    public float ChargePower => chargePower;
    public Vector2 AimOrigin => (Vector2)transform.position + originOffset;
    public Vector2 AimDirection => EffectiveAimDirection;
    public Vector2 EffectiveAimDirection
    {
        get
        {
            float radians = localAimAngleDegrees * Mathf.Deg2Rad;
            return new Vector2(
                Mathf.Cos(radians) * facingSign,
                Mathf.Sin(radians)).normalized;
        }
    }
    public Vector2 TargetPosition => AimOrigin + EffectiveAimDirection * aimRadius;
    public Vector2 ChargingPosition => AimOrigin + EffectiveAimDirection * chargingRadius;
    public int FacingSign => facingSign;
    public float LocalAimAngleDegrees => localAimAngleDegrees;
    public float DirectionAngleDegrees =>
        Mathf.Atan2(EffectiveAimDirection.y, EffectiveAimDirection.x) * Mathf.Rad2Deg;

    private void Awake()
    {
        turnCharacter = GetComponent<TurnCharacterController>();
        characterVisual = GetComponent<CharacterVisual>();
        facingSign = startsFacingRight ? 1 : -1;
        if (characterVisual != null)
        {
            facingSign = characterVisual.IsFacingRight ? 1 : -1;
            characterVisual.FacingChanged += HandleFacingChanged;
        }

        LoadDefaultSprites();
        BuildVisuals();
        SetChargePower(0f);
    }

    private void OnDestroy()
    {
        if (characterVisual != null)
        {
            characterVisual.FacingChanged -= HandleFacingChanged;
        }
    }

    private void Update()
    {
        SyncFacingFromVisual();
        bool visible = turnCharacter != null && turnCharacter.HasControl;
        if (visible)
        {
            ReadAimInput();
        }

        UpdateVisuals(visible);
    }

    public void SetChargePower(float normalizedPower)
    {
        chargePower = Mathf.Clamp01(normalizedPower);
        UpdateChargeMask();
    }

    public void ConfirmFacingFromAim()
    {
        SyncFacingFromVisual();
        characterVisual?.SetFacingRight(facingSign > 0);
    }

    private void ReadAimInput()
    {
        float verticalInput = 0f;
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (keyboard.leftArrowKey.isPressed)
        {
            SetFacingSign(-1);
        }

        if (keyboard.rightArrowKey.isPressed)
        {
            SetFacingSign(1);
        }

        if (keyboard.upArrowKey.isPressed) verticalInput += 1f;
        if (keyboard.downArrowKey.isPressed) verticalInput -= 1f;
#else
        if (Input.GetKey(KeyCode.LeftArrow))
        {
            SetFacingSign(-1);
        }

        if (Input.GetKey(KeyCode.RightArrow))
        {
            SetFacingSign(1);
        }

        if (Input.GetKey(KeyCode.UpArrow)) verticalInput += 1f;
        if (Input.GetKey(KeyCode.DownArrow)) verticalInput -= 1f;
#endif
        localAimAngleDegrees = Mathf.Clamp(
            localAimAngleDegrees + verticalInput * rotateSpeedDegrees * Time.deltaTime,
            -90f,
            90f);
    }

    private void SetFacingSign(int sign)
    {
        facingSign = sign >= 0 ? 1 : -1;
        characterVisual?.SetFacingRight(facingSign > 0);
    }

    private void SyncFacingFromVisual()
    {
        if (characterVisual != null)
        {
            facingSign = characterVisual.IsFacingRight ? 1 : -1;
        }
    }

    private void HandleFacingChanged(bool facingRight)
    {
        facingSign = facingRight ? 1 : -1;
        UpdateVisuals(turnCharacter != null && turnCharacter.HasControl);
    }

    private void BuildVisuals()
    {
        targetingRoot = new GameObject("TargetingVisual").transform;
        targetingRoot.SetParent(transform, false);
        targetingImage = new GameObject("TargetingImage").transform;
        targetingImage.SetParent(targetingRoot, false);
        targetingRenderer = targetingImage.gameObject.AddComponent<SpriteRenderer>();
        targetingRenderer.sprite = targetingSprite;
        targetingRenderer.sortingOrder = visualSortingOrder;

        chargingRoot = new GameObject("ChargingVisual").transform;
        chargingRoot.SetParent(transform, false);
        chargingImage = new GameObject("ChargingImage").transform;
        chargingImage.SetParent(chargingRoot, false);
        chargingRenderer = chargingImage.gameObject.AddComponent<SpriteRenderer>();
        chargingRenderer.sprite = chargingSprite;
        chargingRenderer.sortingOrder = visualSortingOrder - 1;
        chargingRenderer.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;

        chargingMaskTransform = new GameObject("ChargingMask").transform;
        chargingMaskTransform.SetParent(chargingRoot, false);
        chargingMask = chargingMaskTransform.gameObject.AddComponent<SpriteMask>();
        chargingMask.sprite = GetMaskSprite();
        chargingMask.isCustomRangeActive = true;
        chargingMask.frontSortingOrder = visualSortingOrder;
        chargingMask.backSortingOrder = visualSortingOrder - 2;
    }

    private void UpdateVisuals(bool visible)
    {
        if (targetingRoot == null || chargingRoot == null)
        {
            return;
        }

        Vector2 direction = EffectiveAimDirection;
        float directionAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        targetingRoot.gameObject.SetActive(visible);
        targetingRoot.position = AimOrigin + direction * aimRadius;
        targetingRoot.rotation = Quaternion.Euler(
            0f,
            0f,
            directionAngle + targetingSpriteAngleOffset);
        if (targetingRenderer != null)
        {
            targetingRenderer.sprite = targetingSprite;
            targetingImage.localScale = Vector3.one * targetingScale;
            targetingImage.localPosition = GetCenteredOffset(targetingSprite, targetingScale);
        }

        bool showCharging = visible && chargePower > 0.001f;
        chargingRoot.gameObject.SetActive(showCharging);
        chargingRoot.position = AimOrigin + direction * (chargingRadius + 0.08f);
        chargingRoot.rotation = Quaternion.Euler(
            0f,
            0f,
            directionAngle + chargingSpriteAngleOffset);
        if (chargingRenderer != null)
        {
            chargingRenderer.sprite = chargingSprite;
            chargingImage.localScale = Vector3.one * chargingScale;
            chargingImage.localPosition = GetCenteredOffset(chargingSprite, chargingScale);
        }

        UpdateChargeMask();
    }

    private void UpdateChargeMask()
    {
        if (chargingSprite == null || chargingMaskTransform == null)
        {
            return;
        }

        Bounds bounds = chargingSprite.bounds;
        float width = Mathf.Max(0.05f, bounds.size.x * chargingScale * 1.05f);
        float height = Mathf.Max(0.05f, bounds.size.y * chargingScale);
        float fillHeight = Mathf.Max(0.01f, height * Mathf.Clamp01(chargePower));
        float bottom = -height * 0.5f;
        chargingMaskTransform.localPosition = new Vector3(0f, bottom + fillHeight * 0.5f, 0f);
        chargingMaskTransform.localRotation = Quaternion.identity;
        chargingMaskTransform.localScale = new Vector3(width, fillHeight, 1f);
    }

    private static Vector3 GetCenteredOffset(Sprite sprite, float scale)
    {
        return sprite != null ? -sprite.bounds.center * scale : Vector3.zero;
    }

    private static Sprite GetMaskSprite()
    {
        if (maskSprite != null)
        {
            return maskSprite;
        }

        Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        texture.SetPixel(0, 0, Color.white);
        texture.Apply();
        texture.hideFlags = HideFlags.HideAndDontSave;
        maskSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, 1f, 1f),
            new Vector2(0.5f, 0.5f),
            1f);
        maskSprite.hideFlags = HideFlags.HideAndDontSave;
        return maskSprite;
    }

    private void LoadDefaultSprites()
    {
        if (targetingSprite == null)
        {
            targetingSprite = Resources.Load<Sprite>("Sprites/UI/targeting");
        }

        if (chargingSprite == null)
        {
            chargingSprite = Resources.Load<Sprite>("Sprites/UI/charging");
        }

        if (targetingSprite == null)
        {
            targetingSprite = GetFallbackTargetingSprite();
        }

        if (chargingSprite == null)
        {
            chargingSprite = GetFallbackChargingSprite();
        }
    }

    private static Sprite GetFallbackTargetingSprite()
    {
        if (fallbackTargetingSprite != null)
        {
            return fallbackTargetingSprite;
        }

        const int size = 128;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color32[] pixels = new Color32[size * size];
        Color32 color = new Color32(255, 70, 45, 255);
        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 offset = new Vector2(x, y) - center;
                float distance = offset.magnitude;
                bool ring = distance >= 40f && distance <= 47f;
                bool horizontal = Mathf.Abs(offset.y) <= 3f && Mathf.Abs(offset.x) >= 30f;
                bool vertical = Mathf.Abs(offset.x) <= 3f && Mathf.Abs(offset.y) >= 30f;
                pixels[y * size + x] = ring || horizontal || vertical
                    ? color
                    : new Color32(0, 0, 0, 0);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false);
        texture.filterMode = FilterMode.Bilinear;
        texture.hideFlags = HideFlags.HideAndDontSave;
        fallbackTargetingSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            18f);
        fallbackTargetingSprite.hideFlags = HideFlags.HideAndDontSave;
        return fallbackTargetingSprite;
    }

    private static Sprite GetFallbackChargingSprite()
    {
        if (fallbackChargingSprite != null)
        {
            return fallbackChargingSprite;
        }

        const int width = 48;
        const int height = 128;
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        Color32[] pixels = new Color32[width * height];

        for (int y = 0; y < height; y++)
        {
            float t = y / (float)(height - 1);
            Color color = Color.Lerp(
                new Color(1f, 0.18f, 0.1f, 1f),
                new Color(1f, 0.95f, 0.1f, 1f),
                t);
            float halfWidth = Mathf.Lerp(5f, width * 0.48f, t);
            for (int x = 0; x < width; x++)
            {
                float distanceFromCenter = Mathf.Abs(x - (width - 1) * 0.5f);
                pixels[y * width + x] = distanceFromCenter <= halfWidth
                    ? (Color32)color
                    : new Color32(0, 0, 0, 0);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false);
        texture.filterMode = FilterMode.Bilinear;
        texture.hideFlags = HideFlags.HideAndDontSave;
        fallbackChargingSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, width, height),
            new Vector2(0.5f, 0.5f),
            16f);
        fallbackChargingSprite.hideFlags = HideFlags.HideAndDontSave;
        return fallbackChargingSprite;
    }
}
