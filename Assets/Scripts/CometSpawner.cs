using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class CometSpawner : MonoBehaviour
{
    [Header("Spawner Configuration")]
    [Tooltip("Minimum interval between comet spawns in seconds. - 10")]
    public float minSpawnInterval = 10f;
    
    [Tooltip("Maximum interval between comet spawns in seconds.- 15")]
    public float maxSpawnInterval = 15f;

    [Header("Comet Physics")]
    [Tooltip("Minimum movement speed of comets (meters per second). - 20")]
    public float minSpeed = 20f;
    
    [Tooltip("Maximum movement speed of comets (meters per second). - 35")]
    public float maxSpeed = 35f;

    [Tooltip("Minimum sphere diameter of comets.")]
    public float minSize = 0.04f;
    
    [Tooltip("Maximum sphere diameter of comets.")]
    public float maxSize = 0.12f;

    [Header("Visuals & Materials")]
    [Tooltip("Material to assign to the TrailRenderer. If left empty, a default sprite/URP particle material will be created.")]
    public Material cometMaterial;

    [ColorUsage(true, true)]
    [Tooltip("Glow color of the comet trail when relaxed (Cyan HDR).")]
    public Color calmTrailColor = new Color(0.0f, 0.8f, 1.0f, 1.0f);

    [ColorUsage(true, true)]
    [Tooltip("Glow color of the comet trail when stressed (Amber/Orange HDR).")]
    public Color stressedTrailColor = new Color(1.0f, 0.35f, 0.05f, 1.0f);

    [Header("Bpm Connection (Optional)")]
    [Tooltip("If assigned, the comet trail color will adapt to the current BPM.")]
    public BpmClient bpmSource;

    private float _nextSpawnTime;

    void Start()
    {
        ScheduleNextSpawn();
    }

    void Update()
    {
        if (Time.time >= _nextSpawnTime)
        {
            SpawnComet();
            ScheduleNextSpawn();
        }
    }

    private void ScheduleNextSpawn()
    {
        float interval = Random.Range(minSpawnInterval, maxSpawnInterval);
        _nextSpawnTime = Time.time + interval;
    }

    private Vector3 GetPlayerPosition()
    {
        if (Camera.main != null)
        {
            return Camera.main.transform.position;
        }
        return Vector3.zero;
    }

    private void SpawnComet()
    {
        Vector3 playerPos = GetPlayerPosition();
        
        // Choose a random horizontal angle for the starting point
        float theta = Random.Range(0f, 2f * Mathf.PI);
        
        // Starting point on a cylinder around the player (radius 30m, height 18m to 25m)
        float spawnRadius = 30f;
        float startHeight = Random.Range(18f, 25f);
        Vector3 startPos = playerPos + new Vector3(Mathf.Cos(theta) * spawnRadius, startHeight, Mathf.Sin(theta) * spawnRadius);
        
        // Target point on the opposite side of the circle (offset angle by ~180 degrees)
        float targetTheta = theta + Mathf.PI + Random.Range(-Mathf.PI / 6f, Mathf.PI / 6f);
        float endHeight = Random.Range(12f, 17f); // slightly descending flight path
        Vector3 endPos = playerPos + new Vector3(Mathf.Cos(targetTheta) * spawnRadius, endHeight, Mathf.Sin(targetTheta) * spawnRadius);
        
        // Create Comet GameObject (simple sphere)
        GameObject cometGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        cometGo.name = "ShootingStar";
        
        // Remove collider to prevent collisions with the VR player or scene objects
        Collider col = cometGo.GetComponent<Collider>();
        if (col != null) Destroy(col);
        
        // Set size and position
        float size = Random.Range(minSize, maxSize);
        cometGo.transform.localScale = Vector3.one * size;
        cometGo.transform.position = startPos;
        
        // Setup movement script
        Comet movement = cometGo.AddComponent<Comet>();
        movement.speed = Random.Range(minSpeed, maxSpeed);
        movement.direction = (endPos - startPos).normalized;
        
        // Calculate lifetime based on distance and speed
        float distance = Vector3.Distance(startPos, endPos);
        movement.lifetime = distance / movement.speed;
        
        // Configure visuals & Trail
        SetupCometVisuals(cometGo);
    }

    private void SetupCometVisuals(GameObject go)
    {
        // 1. Make the head sphere bright white
        Renderer sphereRenderer = go.GetComponent<Renderer>();
        if (sphereRenderer != null)
        {
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitShader == null) unlitShader = Shader.Find("Sprites/Default");
            
            Material headMat = new Material(unlitShader);
            headMat.color = Color.white;
            sphereRenderer.material = headMat;
        }

        // 2. Add and configure TrailRenderer
        TrailRenderer tr = go.AddComponent<TrailRenderer>();
        tr.time = 0.5f; // how long the tail lasts in seconds
        tr.startWidth = go.transform.localScale.x * 1.5f;
        tr.endWidth = 0.0f; // tapered tail
        tr.numCornerVertices = 2;
        tr.numCapVertices = 2;
        
        // Assign Material
        if (cometMaterial != null)
        {
            tr.material = cometMaterial;
        }
        else
        {
            Shader URPShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            Shader legacyShader = Shader.Find("Sprites/Default");
            tr.material = new Material(URPShader != null ? URPShader : legacyShader);
        }

        // Determine current BPM color for the tail
        Color activeColor = calmTrailColor;
        if (bpmSource != null && bpmSource.LastBpm.HasValue)
        {
            float t = Mathf.InverseLerp(60f, 100f, bpmSource.LastBpm.Value);
            activeColor = Color.Lerp(calmTrailColor, stressedTrailColor, t);
        }
        
        // Setup color gradient: White head -> BPM color body -> Fades to transparent
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { 
                new GradientColorKey(Color.white, 0.0f), 
                new GradientColorKey(activeColor, 0.15f), 
                new GradientColorKey(activeColor, 1.0f) 
            },
            new GradientAlphaKey[] { 
                new GradientAlphaKey(1.0f, 0.0f), 
                new GradientAlphaKey(0.8f, 0.15f), 
                new GradientAlphaKey(0.0f, 1.0f) 
            }
        );
        tr.colorGradient = grad;
    }
}

// Simple helper component to translate the comet in world space
public class Comet : MonoBehaviour
{
    public Vector3 direction;
    public float speed;
    public float lifetime;

    private float _elapsed = 0f;

    void Update()
    {
        transform.Translate(direction * speed * Time.deltaTime, Space.World);
        _elapsed += Time.deltaTime;
        
        if (_elapsed >= lifetime)
        {
            Destroy(gameObject);
        }
    }
}
