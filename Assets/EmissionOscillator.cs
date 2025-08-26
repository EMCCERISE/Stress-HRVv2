using UnityEngine;

[DisallowMultipleComponent]
public class EmissionOscillator : MonoBehaviour
{
    [Header("Alvo")]
    public Renderer targetRenderer;              // se vazio, pega do próprio GO

    [Header("Parâmetros da emissão")]
    public Color color = Color.white;            // cor base da emissão (HDR recomendado)
    [Min(0f)] public float baseIntensity = 1f;   // nível médio
    [Min(0f)] public float amplitude = 1f;       // variação em torno do médio
    [Min(0f)] public float frequencyHz = 1f;     // f (Hz) -> sin(2*pi*f*t)
    public float phaseOffsetRad = 0f;            // fase opcional em radianos

    [Tooltip("Se ligado, também ajusta RenderSettings.ambientIntensity (se Source=Skybox).")]
    public bool alsoAffectAmbient = false;
    public float ambientAtMin = 0.05f;
    public float ambientAtMax = 1.0f;

    Material _mat;
    MaterialPropertyBlock _mpb;
    string _emissionProp = "_EmissionColor";     // URP/Standard
    bool _hasEmission = false;

    void Awake()
    {
        if (!targetRenderer) targetRenderer = GetComponent<Renderer>();
        if (!targetRenderer)
        {
            Debug.LogError("[EmissionOscillator] Sem Renderer.");
            enabled = false; return;
        }

        // Instancia material para não afetar o asset original
        _mat = targetRenderer.material;

        // Descobre propriedade de emissão (URP/Standard usam _EmissionColor; HDRP costuma usar _EmissiveColor)
        if (_mat.HasProperty("_EmissionColor"))
        {
            _emissionProp = "_EmissionColor";
            _hasEmission = true;
            _mat.EnableKeyword("_EMISSION");
        }
        else if (_mat.HasProperty("_EmissiveColor"))
        {
            _emissionProp = "_EmissiveColor";
            _hasEmission = true;
            // Em alguns shaders HDRP, a intensidade é separada; aqui usamos cor * intensidade
        }
        else
        {
            Debug.LogWarning("[EmissionOscillator] Material sem propriedade de emissão conhecida.");
        }

        _mpb = new MaterialPropertyBlock();
    }

    void Update()
    {
        // senóide: base + amp * sin(2π f t + fase)
        float osc = Mathf.Sin(2f * Mathf.PI * frequencyHz * Time.time + phaseOffsetRad);
        float intensity = Mathf.Max(0f, baseIntensity + amplitude * osc);

        if (_hasEmission)
        {
            Color hdr = color * intensity; // HDR: aumente intensidade com cores “acima de 1” se quiser
            targetRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(_emissionProp, hdr);
            targetRenderer.SetPropertyBlock(_mpb);
        }

        if (alsoAffectAmbient)
        {
            // mapeia intensidade normalizada 0..1 para ambient
            float norm = 0.5f * (1f + osc); // senóide normalizada
            RenderSettings.ambientIntensity = Mathf.Lerp(ambientAtMin, ambientAtMax, norm);
        }
    }

    // utilitário: definir frequência a partir de BPM (se quiser ligar depois)
    public void SetBpm(int bpm)
    {
        frequencyHz = Mathf.Max(0f, bpm / 60f);
    }
}
