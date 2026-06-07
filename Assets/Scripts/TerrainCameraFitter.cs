using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public class TerrainCameraFitter : MonoBehaviour
{
    [SerializeField] private TerrainManager terrain;
    [SerializeField, Min(0f)] private float paddingWorld = 0.3f;
    [SerializeField] private bool fitOnStart = true;
    [SerializeField] private bool keepFitting = false;

    private Camera targetCamera;

    private void Awake()
    {
        targetCamera = GetComponent<Camera>();
    }

    private void Start()
    {
        if (fitOnStart)
        {
            FitCameraToTerrain();
        }
    }

    private void LateUpdate()
    {
        if (keepFitting)
        {
            FitCameraToTerrain();
        }
    }

    public void FitCameraToTerrain()
    {
        if (targetCamera == null)
        {
            targetCamera = GetComponent<Camera>();
        }

        if (terrain == null)
        {
            terrain = FindTerrain();
        }

        if (targetCamera == null || terrain == null || terrain.WidthPx <= 0 || terrain.HeightPx <= 0)
        {
            return;
        }

        targetCamera.orthographic = true;

        float widthWorld = terrain.WidthPx / (float)terrain.PixelsPerUnit;
        float heightWorld = terrain.HeightPx / (float)terrain.PixelsPerUnit;
        Vector2 center = terrain.TerrainOriginWorld + new Vector2(widthWorld * 0.5f, heightWorld * 0.5f);
        float aspect = Mathf.Max(0.01f, targetCamera.aspect);
        float sizeByHeight = heightWorld * 0.5f + paddingWorld;
        float sizeByWidth = widthWorld / (2f * aspect) + paddingWorld;

        targetCamera.orthographicSize = Mathf.Max(sizeByHeight, sizeByWidth);
        transform.position = new Vector3(center.x, center.y, transform.position.z);
    }

    private TerrainManager FindTerrain()
    {
#if UNITY_6000_0_OR_NEWER || UNITY_2023_1_OR_NEWER
        return Object.FindAnyObjectByType<TerrainManager>();
#else
        return Object.FindObjectOfType<TerrainManager>();
#endif
    }
}
