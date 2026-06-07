using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class TerrainEditBrush : MonoBehaviour
{
    [SerializeField] private TerrainManager terrain;
    [SerializeField] private Camera worldCamera;
    [SerializeField] private int destroyRadiusPx = 24;
    [SerializeField] private int createRadiusPx = 20;
    [SerializeField] private float bridgeLengthWorldUnits = 5f;
    [SerializeField] private int bridgeThicknessPx = 8;
    [SerializeField] private bool logClicks = true;

    private void Awake()
    {
        if (terrain == null)
        {
            terrain = FindAnyObjectByType<TerrainManager>();
        }

        if (worldCamera == null)
        {
            worldCamera = Camera.main;
        }
    }

    public void Configure(TerrainManager terrainManager, Camera inputCamera)
    {
        terrain = terrainManager;
        worldCamera = inputCamera;
    }

    private void Update()
    {
        if (terrain == null || worldCamera == null)
        {
            return;
        }

#if ENABLE_INPUT_SYSTEM
        HandleInputSystem();
#elif ENABLE_LEGACY_INPUT_MANAGER
        HandleLegacyInput();
#endif
    }

    private void HandleInputSystem()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current == null)
        {
            return;
        }

        Vector2 worldPoint = MouseWorldPoint(Mouse.current.position.ReadValue());

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            bool bridgeMode = Keyboard.current != null &&
                (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);

            if (bridgeMode)
            {
                terrain.CreateBridge(worldPoint, Vector2.right, bridgeLengthWorldUnits, bridgeThicknessPx);
                LogClick("CreateBridge", worldPoint);
            }
            else
            {
                terrain.DestroyCircle(worldPoint, destroyRadiusPx);
                LogClick("DestroyCircle", worldPoint);
            }
        }

        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            terrain.CreateCircle(worldPoint, createRadiusPx);
            LogClick("CreateCircle", worldPoint);
        }

        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
        {
            terrain.ResetTerrain();
            Debug.Log("Terrain reset.");
        }

        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            terrain.RebuildAllColliders();
            Debug.Log("Terrain colliders rebuilt.");
        }
#endif
    }

    private void HandleLegacyInput()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        Vector2 worldPoint = MouseWorldPoint(Input.mousePosition);

        if (Input.GetMouseButtonDown(0))
        {
            bool bridgeMode = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (bridgeMode)
            {
                terrain.CreateBridge(worldPoint, Vector2.right, bridgeLengthWorldUnits, bridgeThicknessPx);
                LogClick("CreateBridge", worldPoint);
            }
            else
            {
                terrain.DestroyCircle(worldPoint, destroyRadiusPx);
                LogClick("DestroyCircle", worldPoint);
            }
        }

        if (Input.GetMouseButtonDown(1))
        {
            terrain.CreateCircle(worldPoint, createRadiusPx);
            LogClick("CreateCircle", worldPoint);
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            terrain.ResetTerrain();
            Debug.Log("Terrain reset.");
        }

        if (Input.GetKeyDown(KeyCode.Space))
        {
            terrain.RebuildAllColliders();
            Debug.Log("Terrain colliders rebuilt.");
        }
#endif
    }

    private Vector2 MouseWorldPoint(Vector2 mouse)
    {
        Vector3 screenPoint = new Vector3(mouse.x, mouse.y, -worldCamera.transform.position.z);
        Vector3 worldPoint = worldCamera.ScreenToWorldPoint(screenPoint);
        return new Vector2(worldPoint.x, worldPoint.y);
    }

    private void LogClick(string action, Vector2 worldPoint)
    {
        if (!logClicks)
        {
            return;
        }

        Vector2Int pixel = terrain.WorldToPixel(worldPoint);
        Debug.Log(action + " at world " + worldPoint + ", pixel " + pixel + ", solid=" + terrain.IsSolidWorld(worldPoint));
    }
}
