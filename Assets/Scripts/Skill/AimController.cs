using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
#if UNITY_EDITOR
using UnityEditor;
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
    [SerializeField, Range(0f, 180f)] private float sideAngleDegrees = 90f;
    [SerializeField] private bool startsFacingRight = true;

    [Header("Visual")]
    [SerializeField, Min(0.01f)] private float targetingScale = 0.18f;
    [SerializeField, Min(0.01f)] private float chargingScale = 0.16f;
    [SerializeField] private int visualSortingOrder = 25;

    private TurnCharacterController turnCharacter;
    private CharacterVisual characterVisual;
    private int sideSign = 1;
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

    public float ChargePower => chargePower;
    public Vector2 AimOrigin => (Vector2)transform.position + originOffset;
    public Vector2 AimDirection => DirectionFromAngle(WorldAngleDegrees);
    public Vector2 TargetPosition => AimOrigin + AimDirection * aimRadius;
    public Vector2 ChargingPosition => AimOrigin + AimDirection * chargingRadius;
    public float WorldAngleDegrees => sideSign >= 0
        ? Mathf.Lerp(-90f, 90f, sideAngleDegrees / 180f)
        : Mathf.Lerp(270f, 90f, sideAngleDegrees / 180f);

    private void Awake()
    {
        turnCharacter = GetComponent<TurnCharacterController>();
        characterVisual = GetComponent<CharacterVisual>();
        sideSign = startsFacingRight ? 1 : -1;
        characterVisual?.SetFacingRight(sideSign > 0);
        LoadDefaultSpritesInEditor();
        BuildVisuals();
        SetChargePower(0f);
    }

    private void Update()
    {
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
        float x = AimDirection.x;
        if (Mathf.Abs(x) > 0.001f)
        {
            sideSign = x >= 0f ? 1 : -1;
            characterVisual?.SetFacingRight(sideSign > 0);
        }
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
            sideSign = -1;
            characterVisual?.SetFacingRight(false);
        }

        if (keyboard.rightArrowKey.isPressed)
        {
            sideSign = 1;
            characterVisual?.SetFacingRight(true);
        }

        if (keyboard.upArrowKey.isPressed) verticalInput += 1f;
        if (keyboard.downArrowKey.isPressed) verticalInput -= 1f;
#else
        if (Input.GetKey(KeyCode.LeftArrow))
        {
            sideSign = -1;
            characterVisual?.SetFacingRight(false);
        }

        if (Input.GetKey(KeyCode.RightArrow))
        {
            sideSign = 1;
            characterVisual?.SetFacingRight(true);
        }

        if (Input.GetKey(KeyCode.UpArrow)) verticalInput += 1f;
        if (Input.GetKey(KeyCode.DownArrow)) verticalInput -= 1f;
#endif
        sideAngleDegrees = Mathf.Clamp(sideAngleDegrees + verticalInput * rotateSpeedDegrees * Time.deltaTime, 0f, 180f);
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

        Vector2 direction = AimDirection;
        float rotation = WorldAngleDegrees - 90f;
        targetingRoot.gameObject.SetActive(visible);
        targetingRoot.position = TargetPosition;
        targetingRoot.rotation = Quaternion.Euler(0f, 0f, rotation);
        if (targetingRenderer != null)
        {
            targetingRenderer.sprite = targetingSprite;
            targetingImage.localScale = Vector3.one * targetingScale;
            targetingImage.localPosition = GetCenteredOffset(targetingSprite, targetingScale);
        }

        bool showCharging = visible && chargePower > 0.001f;
        chargingRoot.gameObject.SetActive(showCharging);
        chargingRoot.position = ChargingPosition + direction * 0.08f;
        chargingRoot.rotation = Quaternion.Euler(0f, 0f, rotation);
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

    private static Vector2 DirectionFromAngle(float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)).normalized;
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
        maskSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        maskSprite.hideFlags = HideFlags.HideAndDontSave;
        return maskSprite;
    }

    private void LoadDefaultSpritesInEditor()
    {
#if UNITY_EDITOR
        if (targetingSprite == null)
        {
            targetingSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Sprites/UI/targeting.png");
        }

        if (chargingSprite == null)
        {
            chargingSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Sprites/UI/charging.png");
        }
#endif
    }
}
