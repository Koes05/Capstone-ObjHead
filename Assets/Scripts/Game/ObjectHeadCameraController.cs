using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class ObjectHeadCameraController : MonoBehaviour
{
    [SerializeField] private TerrainManager terrain;
    [SerializeField] private TurnManager turnManager;
    [SerializeField, Min(0.1f)] private float followLerp = 8f;
    [SerializeField, Min(0.1f)] private float manualPanSpeed = 9f;
    [SerializeField, Min(0.1f)] private float zoomSpeed = 7f;
    [SerializeField, Min(1f)] private float minSize = 4f;
    [SerializeField, Min(1f)] private float defaultPlaySize = 7.5f;
    [SerializeField, Min(1f)] private float maxSize = 13f;
    [SerializeField] private Vector2 paddingWorld = new Vector2(0.4f, 0.4f);

    private Camera targetCamera;
    private Vector3 manualOffset;
    private bool overviewMode;
    private bool forceCharacterFocus;
    private SkillProjectile lastSeenProjectile;

    private void Awake()
    {
        targetCamera = GetComponent<Camera>();
        targetCamera.orthographic = true;
    }

    private void Start()
    {
        if (terrain == null)
        {
            terrain = FindAny<TerrainManager>();
        }

        if (turnManager == null)
        {
            turnManager = FindAny<TurnManager>();
        }

        FocusOnCurrentTarget(true);
    }

    private void LateUpdate()
    {
        if (targetCamera == null)
        {
            return;
        }

        ReadManualInput();

        if (overviewMode)
        {
            transform.position = ClampToTerrain(transform.position);
            return;
        }

        Transform target = FindFollowTarget();
        if (target != null)
        {
            Vector3 desired = new Vector3(target.position.x, target.position.y, transform.position.z) + manualOffset;
            desired = ClampToTerrain(desired);
            transform.position = Vector3.Lerp(transform.position, desired, Time.deltaTime * followLerp);
        }
        else
        {
            transform.position = ClampToTerrain(transform.position);
        }
    }

    public void Configure(TerrainManager terrainManager, TurnManager manager)
    {
        terrain = terrainManager;
        turnManager = manager;
        FocusOnCurrentTarget(true);
    }

    public void FitToTerrainOverview()
    {
        if (targetCamera == null)
        {
            targetCamera = GetComponent<Camera>();
        }

        if (terrain == null || terrain.WidthPx <= 0 || terrain.HeightPx <= 0)
        {
            return;
        }

        float widthWorld = terrain.WidthPx / (float)terrain.PixelsPerUnit;
        float heightWorld = terrain.HeightPx / (float)terrain.PixelsPerUnit;
        Vector2 center = terrain.TerrainOriginWorld + new Vector2(widthWorld * 0.5f, heightWorld * 0.5f);
        float sizeByHeight = heightWorld * 0.5f + paddingWorld.y;
        float sizeByWidth = widthWorld / (2f * Mathf.Max(0.01f, targetCamera.aspect)) + paddingWorld.x;
        targetCamera.orthographicSize = Mathf.Max(minSize, Mathf.Max(sizeByHeight, sizeByWidth));
        manualOffset = Vector3.zero;
        overviewMode = true;
        forceCharacterFocus = false;
        transform.position = ClampToTerrain(new Vector3(center.x, center.y, transform.position.z));
    }

    private void FocusOnCurrentTarget(bool resetZoom)
    {
        if (targetCamera == null)
        {
            targetCamera = GetComponent<Camera>();
        }

        if (resetZoom)
        {
            targetCamera.orthographicSize = Mathf.Clamp(defaultPlaySize, minSize, maxSize);
        }

        overviewMode = false;
        forceCharacterFocus = true;
        Transform target = turnManager != null && turnManager.CurrentCharacter != null
            ? turnManager.CurrentCharacter.transform
            : null;
        if (target != null)
        {
            manualOffset = Vector3.zero;
            transform.position = ClampToTerrain(new Vector3(target.position.x, target.position.y, transform.position.z));
            return;
        }

        FitToTerrainOverview();
    }

    private void ReadManualInput()
    {
        Vector2 pan = Vector2.zero;
        float zoomDelta = 0f;
        bool resetOffset = false;
        bool overview = false;

#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.oKey.isPressed) pan.y += 1f;
            if (keyboard.kKey.isPressed) pan.x -= 1f;
            if (keyboard.lKey.isPressed) pan.y -= 1f;
            if (keyboard.semicolonKey.isPressed) pan.x += 1f;
            if (keyboard.minusKey.isPressed || keyboard.numpadMinusKey.isPressed) zoomDelta += 1f;
            if (keyboard.equalsKey.isPressed || keyboard.numpadPlusKey.isPressed) zoomDelta -= 1f;
            resetOffset = keyboard.iKey.wasPressedThisFrame;
            overview = keyboard.pKey.wasPressedThisFrame;
        }

        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            zoomDelta -= mouse.scroll.ReadValue().y * 0.035f;

            if (mouse.middleButton.isPressed || mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                pan -= delta * 0.018f;
            }
        }
#else
        if (Input.GetKey(KeyCode.O)) pan.y += 1f;
        if (Input.GetKey(KeyCode.K)) pan.x -= 1f;
        if (Input.GetKey(KeyCode.L)) pan.y -= 1f;
        if (Input.GetKey(KeyCode.Semicolon)) pan.x += 1f;
        if (Input.GetKey(KeyCode.Minus) || Input.GetKey(KeyCode.KeypadMinus)) zoomDelta += 1f;
        if (Input.GetKey(KeyCode.Equals) || Input.GetKey(KeyCode.KeypadPlus)) zoomDelta -= 1f;
        zoomDelta -= Input.mouseScrollDelta.y * 0.25f;
        if (Input.GetMouseButton(1) || Input.GetMouseButton(2))
        {
            pan.x -= Input.GetAxisRaw("Mouse X") * 12f;
            pan.y -= Input.GetAxisRaw("Mouse Y") * 12f;
        }
        resetOffset = Input.GetKeyDown(KeyCode.I);
        overview = Input.GetKeyDown(KeyCode.P);
#endif

        if (overview)
        {
            FitToTerrainOverview();
            return;
        }

        if (resetOffset)
        {
            manualOffset = Vector3.zero;
            FocusOnCurrentTarget(false);
            return;
        }

        if (pan.sqrMagnitude > 0f)
        {
            ExitOverviewAtCurrentPosition();
            manualOffset += (Vector3)(pan.normalized * manualPanSpeed * Time.deltaTime);
        }

        if (Mathf.Abs(zoomDelta) > 0f)
        {
            ExitOverviewAtCurrentPosition();
            targetCamera.orthographicSize = Mathf.Clamp(targetCamera.orthographicSize + zoomDelta * zoomSpeed * Time.deltaTime, minSize, maxSize);
            transform.position = ClampToTerrain(transform.position);
        }
    }

    private void ExitOverviewAtCurrentPosition()
    {
        if (!overviewMode)
        {
            return;
        }

        overviewMode = false;
        Transform target = FindFollowTarget();
        manualOffset = target != null
            ? transform.position - new Vector3(target.position.x, target.position.y, transform.position.z)
            : Vector3.zero;
    }

    private Transform FindFollowTarget()
    {
        SkillProjectile projectile = FindAny<SkillProjectile>();
        if (projectile != null && projectile != lastSeenProjectile)
        {
            lastSeenProjectile = projectile;
            forceCharacterFocus = false;
            manualOffset = Vector3.zero;
        }

        if (!forceCharacterFocus && projectile != null && projectile.IsFlying)
        {
            return projectile.transform;
        }

        return turnManager != null && turnManager.CurrentCharacter != null
            ? turnManager.CurrentCharacter.transform
            : null;
    }

    private Vector3 ClampToTerrain(Vector3 position)
    {
        if (terrain == null || targetCamera == null || terrain.WidthPx <= 0 || terrain.HeightPx <= 0)
        {
            return position;
        }

        float halfHeight = targetCamera.orthographicSize;
        float halfWidth = halfHeight * targetCamera.aspect;
        float minX = terrain.TerrainOriginWorld.x + halfWidth - paddingWorld.x;
        float maxX = terrain.TerrainOriginWorld.x + terrain.WidthPx / (float)terrain.PixelsPerUnit - halfWidth + paddingWorld.x;
        float minY = terrain.TerrainOriginWorld.y + halfHeight - paddingWorld.y;
        float maxY = terrain.TerrainOriginWorld.y + terrain.HeightPx / (float)terrain.PixelsPerUnit - halfHeight + paddingWorld.y;

        position.x = minX <= maxX
            ? Mathf.Clamp(position.x, minX, maxX)
            : terrain.TerrainOriginWorld.x + terrain.WidthPx / (2f * terrain.PixelsPerUnit);
        position.y = minY <= maxY
            ? Mathf.Clamp(position.y, minY, maxY)
            : terrain.TerrainOriginWorld.y + terrain.HeightPx / (2f * terrain.PixelsPerUnit);
        return position;
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

