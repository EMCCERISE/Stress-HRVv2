using UnityEngine;

[RequireComponent(typeof(ParticleSystem))]
public class HeartRateParticleController : MonoBehaviour
{
    [Header("BPM Source")]
    [Tooltip("Arraste o QuestBleManager (ou adapte para o BpmClient) aqui")]
    public QuestBleManager bpmSource;

    public enum ControlMode { BPM, HRV_RMSSD }

    [Header("Control Mode")]
    [Tooltip("Escolha se a cena reage à média de batimentos (BPM) ou à variabilidade (HRV/RMSSD)")]
    public ControlMode mode = ControlMode.HRV_RMSSD;

    [Header("BPM Range (Se modo = BPM)")]
    public float bpmRelaxed = 60f;
    public float bpmStressed = 100f;

    [Header("HRV Range (Se modo = HRV_RMSSD)")]
    [Tooltip("RMSSD em milissegundos. Valores altos indicam relaxamento. Valores baixos indicam estresse.")]
    public float rmssdRelaxed = 50f; // Ex: 50ms (muito calmo/coerente)
    public float rmssdStressed = 15f; // Ex: 15ms (rígido/estressado)

    [Header("Estratégia Visual (Relaxado -> Estressado)")]
    
    [Tooltip("Cor das partículas. Azul/Verde induz calma (Parassimpático).")]
    public Color colorRelaxed = new Color(0.2f, 0.8f, 1f, 1f); // Azul claro/Ciano
    public Color colorStressed = new Color(1f, 0.5f, 0.2f, 1f); // Laranja avermelhado
    
    [Space]
    [Tooltip("Velocidade da simulação. Mais lento induz ritmo respiratório calmo.")]
    public float speedRelaxed = 0.3f;
    public float speedStressed = 1.5f;

    [Space]
    [Tooltip("Força da turbulência (Noise). Valores baixos = fluxo laminar (segurança).")]
    public float noiseRelaxed = 0.05f;
    public float noiseStressed = 1.0f;

    [Space]
    [Tooltip("Raio de emissão. Raio maior em relaxamento reduz claustrofobia.")]
    public float shapeRadiusRelaxed = 15f;
    public float shapeRadiusStressed = 5f;

    [Header("Suavização")]
    public float responseSpeed = 1.5f;

    private ParticleSystem _particleSystem;
    private ParticleSystem.MainModule _mainModule;
    private ParticleSystem.NoiseModule _noiseModule;
    private ParticleSystem.ShapeModule _shapeModule;

    private float _targetT = 0f; // 0 = Relaxado, 1 = Estressado
    private float _currentT = 0f;
    private int _fallbackBpm = 70;

    void Awake()
    {
        _particleSystem = GetComponent<ParticleSystem>();
        _mainModule = _particleSystem.main;
        _noiseModule = _particleSystem.noise;
        _shapeModule = _particleSystem.shape;
    }

    void OnEnable()
    {
        if (bpmSource != null)
        {
            bpmSource.OnBpmChanged += HandleBpmChanged;
            bpmSource.OnRmssdChanged += HandleRmssdChanged;
            
            if (mode == ControlMode.BPM)
            {
                if (bpmSource.LastBpm.HasValue)
                    HandleBpmChanged(bpmSource.LastBpm.Value);
                else
                    HandleBpmChanged(_fallbackBpm);
            }
            else if (mode == ControlMode.HRV_RMSSD)
            {
                if (bpmSource.LastRmssd.HasValue)
                    HandleRmssdChanged(bpmSource.LastRmssd.Value);
                else
                    HandleRmssdChanged(rmssdRelaxed / 2f); // fallback mediano
            }
        }
    }

    void OnDisable()
    {
        if (bpmSource != null)
        {
            bpmSource.OnBpmChanged -= HandleBpmChanged;
            bpmSource.OnRmssdChanged -= HandleRmssdChanged;
        }
    }

    private void HandleBpmChanged(int? bpm)
    {
        if (mode == ControlMode.BPM)
        {
            float val = bpm ?? _fallbackBpm;
            float clamped = Mathf.Clamp(val, bpmRelaxed, bpmStressed);
            // targetT 0 = Relaxado (bpmRelaxed), 1 = Estressado (bpmStressed)
            _targetT = Mathf.InverseLerp(bpmRelaxed, bpmStressed, clamped);
        }
    }

    private void HandleRmssdChanged(float? rmssd)
    {
        if (mode == ControlMode.HRV_RMSSD && rmssd.HasValue)
        {
            // RMSSD Alto = Relaxado (targetT = 0)
            // RMSSD Baixo = Estressado (targetT = 1)
            float min = Mathf.Min(rmssdRelaxed, rmssdStressed);
            float max = Mathf.Max(rmssdRelaxed, rmssdStressed);
            float clamped = Mathf.Clamp(rmssd.Value, min, max);
            
            // InverseLerp mapeia de 'a' para 'b' retornando 0 a 1.
            _targetT = Mathf.InverseLerp(rmssdRelaxed, rmssdStressed, clamped);
        }
    }

    void Update()
    {
        // Interpolação suave para evitar saltos bruscos no visual quando o BPM muda de repente
        _currentT = Mathf.Lerp(_currentT, _targetT, Time.deltaTime * responseSpeed);

        // 1. Transição de Cor
        _mainModule.startColor = Color.Lerp(colorRelaxed, colorStressed, _currentT);

        // 2. Velocidade de Simulação (Entrainment)
        _mainModule.simulationSpeed = Mathf.Lerp(speedRelaxed, speedStressed, _currentT);

        // 3. Força da Turbulência / Noise
        if (_noiseModule.enabled)
        {
            ParticleSystem.MinMaxCurve strength = _noiseModule.strength;
            strength.constant = Mathf.Lerp(noiseRelaxed, noiseStressed, _currentT);
            _noiseModule.strength = strength;
        }

        // 4. Raio de Emissão (Expansão espacial)
        if (_shapeModule.enabled)
        {
            _shapeModule.radius = Mathf.Lerp(shapeRadiusRelaxed, shapeRadiusStressed, _currentT);
        }
    }
}
