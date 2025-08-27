using System;
using UnityEngine;

[DisallowMultipleComponent]
public class HeartRateLightController : MonoBehaviour
{
    [Header("Fonte de BPM (opcional)")]
    // Se você ligar isso ao seu BpmClient (ver patch abaixo), ele assina o evento e recebe BPM automaticamente.
    public BpmClient bpmSource;

    [Header("Luzes controladas")]
    public Light[] targetLights;

    [Header("Mapeamento BPM → Intensidade")]
    [Tooltip("BPM mínimo (relaxado)")]
    public float bpmMin = 50f;
    [Tooltip("BPM máximo (agitado)")]
    public float bpmMax = 100f;

    [Tooltip("Intensidade quando BPM=bpmMin")]
    public float intensityMin = 0.2f;
    [Tooltip("Intensidade quando BPM=bpmMax")]
    public float intensityMax = 1.5f;

    [Tooltip("Curva opcional para deixar a resposta não-linear (0→1 de BPM normalizado)")]
    public AnimationCurve mappingCurve = AnimationCurve.Linear(0, 0, 1, 1);

    [Header("Suavização")]
    [Tooltip("Velocidade de resposta. Maior = responde mais rápido, menor = mais suave.")]
    [Range(0.1f, 10f)]
    public float responseSpeed = 3f;

    [Header("Ambiente (opcional)")]
    public bool alsoAffectAmbient = false;
    public float ambientMin = 0.5f;
    public float ambientMax = 2.0f;

    [Header("Comportamento em dados ausentes")]
    [Tooltip("Se true, mantém a última intensidade quando BPM vier null/indisponível.")]
    public bool holdOnNull = true;
    [Tooltip("Se false e BPM vier null, usa este BPM como fallback.")]
    public int fallbackBpm = 60;

    // ---- estado interno ----
    private float _currentIntensity;
    private float _targetIntensity;

    void Awake()
    {
        // inicia na intensidade correspondente ao fallback
        SetTargetFromBpm(fallbackBpm);
        _currentIntensity = _targetIntensity;
    }

    void OnEnable()
    {
        // Se um BpmClient for ligado aqui, assina o evento
        if (bpmSource != null)
            bpmSource.OnBpmChanged += OnBpmChanged;
    }

    void OnDisable()
    {
        if (bpmSource != null)
            bpmSource.OnBpmChanged -= OnBpmChanged;
    }

    void Update()
    {
        // Suaviza a intensidade para a meta
        // Lerp amortecido usando uma constante de tempo (1 - e^(-k*dt))
        float k = responseSpeed;
        float a = 1f - Mathf.Exp(-k * Time.deltaTime);
        _currentIntensity = Mathf.Lerp(_currentIntensity, _targetIntensity, a);
        ApplyToLights(_currentIntensity);
    }

    // --- API pública: você pode chamar isso de qualquer outro script, se preferir injetar o BPM manualmente.
    public void SetBpm(int bpm)
    {
        SetTargetFromBpm(bpm);
    }

    // --- handler do evento do BpmClient ---
    private void OnBpmChanged(int? bpm)
    {
        if (bpm.HasValue)
        {
            SetTargetFromBpm(bpm.Value);
        }
        else if (!holdOnNull)
        {
            SetTargetFromBpm(fallbackBpm);
        }
        // se holdOnNull = true, mantemos a _targetIntensity atual
    }

    private void SetTargetFromBpm(int bpm)
    {
        // Normaliza BPM dentro do range [bpmMin, bpmMax]
        float clamped = Mathf.Clamp(bpm, (int)bpmMin, (int)bpmMax);
        float t = Mathf.InverseLerp(bpmMin, bpmMax, clamped);
        // passa pela curva (para respostas log/exp, etc.)
        t = mappingCurve.Evaluate(t);
        // converte para intensidade alvo
        _targetIntensity = Mathf.Lerp(intensityMin, intensityMax, t);
    }

    private void ApplyToLights(float intensity)
    {
        if (targetLights != null)
        {
            for (int i = 0; i < targetLights.Length; i++)
            {
                var L = targetLights[i];
                if (L != null) L.intensity = intensity;
            }
        }

        if (alsoAffectAmbient)
        {
            // mapeia a intensidade atual para ambientIntensity proporcionalmente ao range definido
            float t = Mathf.InverseLerp(intensityMin, intensityMax, Mathf.Clamp(intensity, intensityMin, intensityMax));
            RenderSettings.ambientIntensity = Mathf.Lerp(ambientMin, ambientMax, t);
        }
    }
}
