using UnityEngine;
using UnityEngine.Rendering; // AmbientMode
using TMPro;

public class BpmAmbientRamp : MonoBehaviour
{
    [Header("Referências")]
    public Light directional;
    public TextMeshProUGUI bpmText;

    [Header("Point Lights (opcional)")]
    public bool alsoAffectPointLights = true;
    public Light[] pointLights;                 // arraste suas Point Lights aqui
    [Tooltip("Escala multiplicativa aplicada às intensidades originais das Point Lights no início e no fim.")]
    public float pointScaleAtStart = 1.0f;      // 1.0 = intensidade original
    public float pointScaleAtEnd = 0.25f;     // ex.: 25% da intensidade original no fim

    [Header("Simulação de BPM")]
    public int startBpm = 90;
    public int endBpm = 60;
    public float durationSeconds = 60f;
    public bool loop = true;

    [Header("Luz direcional (quando BPM...)")]
    public float directionalAtStart = 1.2f;
    public float directionalAtEnd = 0.15f;

    [Header("Ambiente")]
    public bool alsoAffectAmbient = true;
    public float ambientAtStart = 1.0f;
    public float ambientAtEnd = 0.05f;
    public bool updateGI = false;

    float elapsed;
    int currentBpm;

    // Guarda estado inicial para Color/Gradient
    AmbientMode initialMode;
    Color initialAmbientLight;
    Color initialSky, initialEquator, initialGround;

    // Guarda intensidades originais das point lights
    float[] pointInitialIntensities;

    void OnEnable()
    {
        initialMode = UnityEngine.RenderSettings.ambientMode;
        initialAmbientLight = UnityEngine.RenderSettings.ambientLight;
        initialSky = UnityEngine.RenderSettings.ambientSkyColor;
        initialEquator = UnityEngine.RenderSettings.ambientEquatorColor;
        initialGround = UnityEngine.RenderSettings.ambientGroundColor;

        // cache das intensidades originais das point lights
        if (alsoAffectPointLights && pointLights != null && pointLights.Length > 0)
        {
            pointInitialIntensities = new float[pointLights.Length];
            for (int i = 0; i < pointLights.Length; i++)
                if (pointLights[i] != null)
                    pointInitialIntensities[i] = pointLights[i].intensity;
        }

        elapsed = 0f;
        Apply(0f);
    }

    void Update()
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / durationSeconds);
        Apply(t);
        if (t >= 1f && loop) elapsed = 0f;
    }

    void Apply(float t)
    {
        // BPM simulado
        float bpmF = Mathf.Lerp(startBpm, endBpm, t);
        currentBpm = Mathf.RoundToInt(bpmF);
        if (bpmText) bpmText.text = $"BPM (sim): {currentBpm}";

        // Direcional
        float dirI = Mathf.Lerp(directionalAtStart, directionalAtEnd, t);
        if (directional) directional.intensity = dirI;

        // Point Lights (escala multiplicativa das intensidades originais)
        if (alsoAffectPointLights && pointLights != null && pointInitialIntensities != null)
        {
            float scale = Mathf.Lerp(pointScaleAtStart, pointScaleAtEnd, t);
            for (int i = 0; i < pointLights.Length; i++)
                if (pointLights[i] != null)
                    pointLights[i].intensity = pointInitialIntensities[i] * scale;
        }

        // Ambiente
        if (!alsoAffectAmbient) return;

        float amb = Mathf.Lerp(ambientAtStart, ambientAtEnd, t);
        switch (UnityEngine.RenderSettings.ambientMode)
        {
            case AmbientMode.Skybox:
                UnityEngine.RenderSettings.ambientIntensity = amb;
                if (updateGI) DynamicGI.UpdateEnvironment();
                break;
            case AmbientMode.Flat:
                UnityEngine.RenderSettings.ambientLight = initialAmbientLight * amb;
                break;
            case AmbientMode.Trilight:
                UnityEngine.RenderSettings.ambientSkyColor = initialSky * amb;
                UnityEngine.RenderSettings.ambientEquatorColor = initialEquator * amb;
                UnityEngine.RenderSettings.ambientGroundColor = initialGround * amb;
                break;
        }
    }
}
