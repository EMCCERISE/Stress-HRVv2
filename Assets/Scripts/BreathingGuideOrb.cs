using System;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class BreathingGuideOrb : MonoBehaviour
{
    [Header("Bpm Connection")]
    [Tooltip("Source client for BPM updates. If null, will run at the default calm state.")]
    public BpmClient bpmSource;

    [Header("Breathing Settings")]
    [Tooltip("Duration of a single full breath (inhale + hold + exhale + hold) in seconds.")]
    [Range(4f, 15f)]
    public float breathingPeriod = 10f; // 0.1 Hz is exactly 10 seconds

    [Tooltip("Minimum scale multiplier of the orb (fully exhaled).")]
    public float minScaleMultiplier = 0.8f;
    [Tooltip("Maximum scale multiplier of the orb (fully inhaled).")]
    public float maxScaleMultiplier = 1.3f;

    [Tooltip("Custom curve representing the breathing cycle. Time: 0 to 1, Value: 0 (exhaled) to 1 (inhaled).")]
    public AnimationCurve breathingCurve = new AnimationCurve(
        new Keyframe(0.0f, 0.0f, 0.0f, 2.5f),      // Inhale starts
        new Keyframe(0.45f, 1.0f, 0.0f, 0.0f),     // Inhale peaks (hold starts)
        new Keyframe(0.55f, 1.0f, 0.0f, 0.0f),     // Hold ends (exhale starts)
        new Keyframe(1.0f, 0.0f, -2.5f, 0.0f)      // Exhale completes
    );

    [Header("UI Instructions (Optional)")]
    [Tooltip("Optional TextMeshPro component to display breathing instructions (Inspire/Expire/Segure).")]
    public TMP_Text instructionText;

    [Tooltip("Portuguese localization for 'Inhale'.")]
    public string inhaleText = "Inspire";
    [Tooltip("Portuguese localization for 'Exhale'.")]
    public string exhaleText = "Expire";
    [Tooltip("Portuguese localization for 'Hold'.")]
    public string holdText = "Segure";

    [Header("UI Scale Correction")]
    [Tooltip("If the Text or Canvas is a child of the Orb, assign it here (or its parent Canvas) to prevent it from scaling with the Orb.")]
    public Transform uiContainerToNormalize;
    
    [Tooltip("If true, the text will pulse in size slightly with the breath. If false, it remains static.")]
    public bool animateTextScale = false;

    [Header("Biofeedback Mapping")]
    [Tooltip("Heart rate representing deep relaxation.")]
    public float bpmMin = 60f;
    [Tooltip("Heart rate representing heightened activity or stress.")]
    public float bpmMax = 100f;

    [ColorUsage(true, true)]
    [Tooltip("Color of the orb when relaxed (Cyan HDR).")]
    public Color calmColor = new Color(0.0f, 0.8f, 1.0f, 1.0f);
    
    [ColorUsage(true, true)]
    [Tooltip("Color of the orb when stressed (Amber/Orange HDR).")]
    public Color stressedColor = new Color(1.0f, 0.35f, 0.05f, 1.0f);

    [Range(0.1f, 10f)]
    [Tooltip("How fast the orb color adapts to BPM changes.")]
    public float biofeedbackSmoothing = 2.0f;

    [Header("Breathing Glow Intensity")]
    [Tooltip("Emission glow intensity when fully exhaled (orb is at its smallest size). Lower is softer.")]
    public float glowIntensityMin = 0.3f;

    [Tooltip("Emission glow intensity when fully inhaled (orb is at its largest size). Higher is brighter.")]
    public float glowIntensityMax = 2.5f;

    [Header("Camera Auto-Positioning")]
    [Tooltip("If true, the orb will find the Main Camera and position itself in front of it.")]
    public bool autoPositionInFront = true;

    [Tooltip("If true, the orb will smoothly follow the player's view. If false, it remains static in world space after start.")]
    public bool followCamera = false;

    [Tooltip("Distance in front of the camera to position the orb.")]
    public float distanceFromCamera = 2.0f;

    [Tooltip("Vertical height offset relative to the camera center.")]
    public float heightOffset = -0.2f;

    [Tooltip("Speed at which the orb follows the camera (used if followCamera is true).")]
    public float followSpeed = 2.0f;

    [Tooltip("Explicit camera transform. If null, will default to Camera.main.")]
    public Transform customCameraTransform;

    // References and Internal State
    private Renderer _renderer;
    private MaterialPropertyBlock _mpb;
    private string _emissionProp = "_EmissionColor";
    private bool _hasEmission = false;
    private Vector3 _baseScale;

    private float _currentBpmNormalized = 0f;
    private float _targetBpmNormalized = 0f;
    private Transform _camTransform;
    private Vector3 _baseUiScale;

    void Awake()
    {
        _baseScale = transform.localScale;
        _renderer = GetComponent<Renderer>();
        _mpb = new MaterialPropertyBlock();

        if (_renderer != null)
        {
            Material mat = _renderer.material; // Instance material
            if (mat.HasProperty("_EmissionColor"))
            {
                _emissionProp = "_EmissionColor";
                _hasEmission = true;
                mat.EnableKeyword("_EMISSION");
            }
            else if (mat.HasProperty("_EmissiveColor"))
            {
                _emissionProp = "_EmissiveColor";
                _hasEmission = true;
            }
        }
    }

    void Start()
    {
        if (customCameraTransform != null)
        {
            _camTransform = customCameraTransform;
        }
        else if (Camera.main != null)
        {
            _camTransform = Camera.main.transform;
        }
        else
        {
            // Try to find OVR Camera Rig eye anchor if Camera.main is not tagged
            GameObject eyeAnchor = GameObject.Find("CenterEyeAnchor");
            if (eyeAnchor != null) _camTransform = eyeAnchor.transform;
        }

        if (autoPositionInFront && _camTransform != null)
        {
            SnapToCameraFront();
        }

        // Auto-detect UI Container to normalize if not assigned
        if (uiContainerToNormalize == null && instructionText != null)
        {
            Canvas canvas = instructionText.GetComponentInParent<Canvas>();
            if (canvas != null && canvas.transform != transform)
            {
                uiContainerToNormalize = canvas.transform;
            }
            else
            {
                uiContainerToNormalize = instructionText.transform;
            }
        }

        if (uiContainerToNormalize != null)
        {
            _baseUiScale = uiContainerToNormalize.localScale;
        }
    }

    void OnEnable()
    {
        if (bpmSource != null)
        {
            bpmSource.OnBpmChanged += OnBpmChanged;
            if (bpmSource.LastBpm.HasValue)
            {
                OnBpmChanged(bpmSource.LastBpm.Value);
            }
        }
    }

    void OnDisable()
    {
        if (bpmSource != null)
        {
            bpmSource.OnBpmChanged -= OnBpmChanged;
        }
    }

    void Update()
    {
        // 1. Smooth BPM interpolation
        _currentBpmNormalized = Mathf.Lerp(
            _currentBpmNormalized, 
            _targetBpmNormalized, 
            biofeedbackSmoothing * Time.deltaTime
        );

        // 2. Animate breathing (Scale)
        float timeInCycle = Time.time % breathingPeriod;
        float normalizedTime = timeInCycle / breathingPeriod;
        
        // Evaluate scale factor from curve
        float curveValue = breathingCurve.Evaluate(normalizedTime);
        float scaleMultiplier = Mathf.Lerp(minScaleMultiplier, maxScaleMultiplier, curveValue);
        transform.localScale = _baseScale * scaleMultiplier;

        // 3. Update Text Instructions and animate text size/alpha
        UpdateTextInstructions(normalizedTime, curveValue, scaleMultiplier);

        // 4. Update Orb Color & Intensity (Biofeedback + Breathing)
        UpdateOrbColor(curveValue);

        // 5. Follow camera smoothly if enabled
        if (followCamera && _camTransform != null)
        {
            Vector3 targetPos = GetTargetPosition();
            transform.position = Vector3.Lerp(transform.position, targetPos, followSpeed * Time.deltaTime);

            // Make the orb (and its text/front face) face the camera
            transform.LookAt(_camTransform);
            transform.Rotate(0, 180, 0);
        }
    }

    private void SnapToCameraFront()
    {
        if (_camTransform == null) return;
        transform.position = GetTargetPosition();
        transform.LookAt(_camTransform);
        transform.Rotate(0, 180, 0);
    }

    private Vector3 GetTargetPosition()
    {
        // Calculate position in front of the camera
        Vector3 forward = _camTransform.forward;
        forward.y = 0; // Keep horizontal alignment
        forward.Normalize();

        return _camTransform.position + forward * distanceFromCamera + Vector3.up * heightOffset;
    }

    private void OnBpmChanged(int? bpm)
    {
        int activeBpm = bpm ?? 60;
        _targetBpmNormalized = Mathf.InverseLerp(bpmMin, bpmMax, activeBpm);
    }

    private void UpdateTextInstructions(float normalizedTime, float curveValue, float scaleMultiplier)
    {
        if (instructionText == null) return;

        // Determine current breathing phase
        string activeInstruction = "";
        
        // Based on our default curve (Inhale: 0-45%, Hold: 45-55%, Exhale: 55-100%)
        if (normalizedTime < 0.45f)
        {
            activeInstruction = inhaleText;
        }
        else if (normalizedTime >= 0.45f && normalizedTime < 0.55f)
        {
            activeInstruction = holdText;
        }
        else
        {
            activeInstruction = exhaleText;
        }

        // Apply text instruction
        instructionText.text = activeInstruction;

        // Soft fade out on holds for more relaxing feedback
        float targetAlpha = (normalizedTime >= 0.45f && normalizedTime < 0.55f) ? 0.7f : 1.0f;
        Color textColor = instructionText.color;
        textColor.a = Mathf.Lerp(textColor.a, targetAlpha, 5f * Time.deltaTime);
        instructionText.color = textColor;

        // UI Scale Correction (Counter-scales the UI container to cancel out the Orb's scale)
        if (uiContainerToNormalize != null && uiContainerToNormalize.IsChildOf(transform))
        {
            float textScaleFactor = 1f;
            if (animateTextScale)
            {
                textScaleFactor = Mathf.Lerp(0.85f, 1.15f, curveValue);
            }
            
            // Set scale and divide by the orb's current scaleMultiplier to keep it stable
            uiContainerToNormalize.localScale = (_baseUiScale * textScaleFactor) / scaleMultiplier;
        }
    }

    private void UpdateOrbColor(float curveValue)
    {
        if (!_hasEmission || _renderer == null) return;

        // 1. Interpolate base colors based on BPM normalization
        Color baseColor = Color.Lerp(calmColor, stressedColor, _currentBpmNormalized);

        // 2. Modulate glow intensity based on breath expansion (curveValue)
        float activeIntensity = Mathf.Lerp(glowIntensityMin, glowIntensityMax, curveValue);
        Color targetColor = baseColor * activeIntensity;

        // Assign via MaterialPropertyBlock to optimize performance
        _renderer.GetPropertyBlock(_mpb);
        _mpb.SetColor(_emissionProp, targetColor);
        _renderer.SetPropertyBlock(_mpb);
    }
}
