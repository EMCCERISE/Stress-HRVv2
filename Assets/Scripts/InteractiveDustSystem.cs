using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

[DisallowMultipleComponent]
public class InteractiveDustSystem : MonoBehaviour
{
    [Header("Bpm Connection")]
    [Tooltip("Source client for BPM updates. If null, will run at the default calm state.")]
    public BpmClient bpmSource;

    [Header("Controller Anchors")]
    [Tooltip("Transform of the left controller. If null, will search for 'LeftControllerAnchor' in the scene.")]
    public Transform leftController;
    [Tooltip("Transform of the right controller. If null, will search for 'RightControllerAnchor' in the scene.")]
    public Transform rightController;

    [Header("Particle Systems")]
    [Tooltip("Particle system for the left hand. If null, one will be created automatically.")]
    public ParticleSystem leftParticleSystem;
    [Tooltip("Particle system for the right hand. If null, one will be created automatically.")]
    public ParticleSystem rightParticleSystem;

    [Header("Emission Settings")]
    [Tooltip("Base emission rate when the controllers are stationary.")]
    public float baseEmissionRate = 2f;
    [Tooltip("Multiplier for particle emission based on controller movement speed.")]
    public float speedEmissionMultiplier = 40f;
    [Tooltip("Maximum emission rate allowed from movement to prevent clutter.")]
    public float maxEmissionRate = 120f;
    [Tooltip("Extra particles emitted per second when holding the trigger button.")]
    public float triggerBonusEmission = 50f;

    [Header("Biofeedback Mapping")]
    [Tooltip("Heart rate representing deep relaxation.")]
    public float bpmMin = 60f;
    [Tooltip("Heart rate representing heightened activity or stress.")]
    public float bpmMax = 100f;

    [Header("Calm State Config (Low BPM)")]
    [ColorUsage(true, true)]
    [Tooltip("Color of the dust when relaxed (cyan/teal HDR is recommended).")]
    public Color calmColor = new Color(0.0f, 0.8f, 1.0f, 1.0f);
    [Tooltip("Particle lifetime when relaxed (longer, drifting gently).")]
    public float calmLifetime = 4.0f;
    [Tooltip("Turbulence/Noise strength when relaxed (low, steady drift).")]
    public float calmNoiseStrength = 0.2f;

    [Header("Stressed State Config (High BPM)")]
    [ColorUsage(true, true)]
    [Tooltip("Color of the dust when stressed (warm amber/orange HDR is recommended).")]
    public Color stressedColor = new Color(1.0f, 0.35f, 0.05f, 1.0f);
    [Tooltip("Particle lifetime when stressed (shorter, fast dissipating).")]
    public float stressedLifetime = 1.5f;
    [Tooltip("Turbulence/Noise strength when stressed (high, erratic movement).")]
    public float stressedNoiseStrength = 1.5f;

    [Header("Smoothing")]
    [Range(0.1f, 10f)]
    [Tooltip("How fast the particles adapt to BPM changes.")]
    public float biofeedbackSmoothing = 2.0f;

    // State variables
    private Vector3 _prevLeftPos;
    private Vector3 _prevRightPos;
    private float _leftVelocity;
    private float _rightVelocity;

    private float _currentBpmNormalized = 0f; // 0 = calm, 1 = stressed
    private float _targetBpmNormalized = 0f;

    void Start()
    {
        // Auto-detect controller anchors if not assigned
        if (leftController == null)
        {
            GameObject go = GameObject.Find("LeftControllerAnchor");
            if (go != null) leftController = go.transform;
            else leftController = FindAnchorBySubstring("left");
        }
        if (rightController == null)
        {
            GameObject go = GameObject.Find("RightControllerAnchor");
            if (go != null) rightController = go.transform;
            else rightController = FindAnchorBySubstring("right");
        }

        // Cache initial positions
        if (leftController != null) _prevLeftPos = leftController.position;
        if (rightController != null) _prevRightPos = rightController.position;

        // Auto-create/configure particle systems if not assigned
        SetupParticleSystem(ref leftParticleSystem, leftController, "LeftHand_DustParticles");
        SetupParticleSystem(ref rightParticleSystem, rightController, "RightHand_DustParticles");
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
        // Smoothly update biofeedback value
        _currentBpmNormalized = Mathf.Lerp(
            _currentBpmNormalized, 
            _targetBpmNormalized, 
            biofeedbackSmoothing * Time.deltaTime
        );

        // Update physics/movement parameters
        CalculateVelocities();

        // Check controller buttons (Triggers)
        bool leftTrigger = IsTriggerPressed(XRNode.LeftHand);
        bool rightTrigger = IsTriggerPressed(XRNode.RightHand);

        // Update particle system settings & emission rates
        UpdateParticleSystem(leftParticleSystem, _leftVelocity, leftTrigger);
        UpdateParticleSystem(rightParticleSystem, _rightVelocity, rightTrigger);
    }

    private void OnBpmChanged(int? bpm)
    {
        int activeBpm = bpm ?? 60;
        _targetBpmNormalized = Mathf.InverseLerp(bpmMin, bpmMax, activeBpm);
    }

    private void CalculateVelocities()
    {
        if (leftController != null)
        {
            Vector3 currentPos = leftController.position;
            // Avoid frame rate spikes
            if (Time.deltaTime > 0f)
            {
                float calculated = (currentPos - _prevLeftPos).magnitude / Time.deltaTime;
                _leftVelocity = Mathf.Lerp(_leftVelocity, calculated, 10f * Time.deltaTime);
            }
            _prevLeftPos = currentPos;
        }

        if (rightController != null)
        {
            Vector3 currentPos = rightController.position;
            if (Time.deltaTime > 0f)
            {
                float calculated = (currentPos - _prevRightPos).magnitude / Time.deltaTime;
                _rightVelocity = Mathf.Lerp(_rightVelocity, calculated, 10f * Time.deltaTime);
            }
            _prevRightPos = currentPos;
        }
    }

    private bool IsTriggerPressed(XRNode handNode)
    {
        var devices = new List<InputDevice>();
        InputDevices.GetDevicesAtXRNode(handNode, devices);
        if (devices.Count > 0)
        {
            InputDevice device = devices[0];
            if (device.TryGetFeatureValue(CommonUsages.triggerButton, out bool pressed))
            {
                return pressed;
            }
        }
        return false;
    }

    private void UpdateParticleSystem(ParticleSystem ps, float velocity, bool triggerActive)
    {
        if (ps == null) return;

        // Calculate dynamic values based on BPM normalization (calm to stressed)
        Color activeColor = Color.Lerp(calmColor, stressedColor, _currentBpmNormalized);
        float activeLifetime = Mathf.Lerp(calmLifetime, stressedLifetime, _currentBpmNormalized);
        float activeNoiseStrength = Mathf.Lerp(calmNoiseStrength, stressedNoiseStrength, _currentBpmNormalized);

        // Modify Main module
        var main = ps.main;
        main.startColor = activeColor;
        main.startLifetime = activeLifetime;

        // Modify Noise module
        var noise = ps.noise;
        noise.strengthMultiplier = activeNoiseStrength;

        // Calculate emission rate: base + movement + trigger bonus
        float targetEmission = baseEmissionRate + (velocity * speedEmissionMultiplier);
        targetEmission = Mathf.Min(targetEmission, maxEmissionRate);
        if (triggerActive)
        {
            targetEmission += triggerBonusEmission;
            
            // Visual feedback on trigger: make particles slightly larger and faster
            main.startSizeMultiplier = 1.8f;
            main.startSpeedMultiplier = 1.5f;
        }
        else
        {
            main.startSizeMultiplier = 1.0f;
            main.startSpeedMultiplier = 0.2f; // drift slowly
        }

        // Apply emission rate
        var emission = ps.emission;
        emission.rateOverTime = targetEmission;
    }

    private void SetupParticleSystem(ref ParticleSystem ps, Transform parent, string defaultName)
    {
        if (ps != null)
        {
            // Ensure world space simulation is active so particles trail properly
            var mainModule = ps.main;
            mainModule.simulationSpace = ParticleSystemSimulationSpace.World;
            return;
        }

        // If no parent transform is found, spawn in world coordinates
        Transform spawnParent = parent != null ? parent : this.transform;

        // Create new GameObject
        GameObject go = new GameObject(defaultName);
        go.transform.SetParent(spawnParent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;

        ps = go.AddComponent<ParticleSystem>();

        // Configure Particle System Modules for elegant glowing dust look
        var main = ps.main;
        main.duration = 10f;
        main.loop = true;
        main.startLifetime = calmLifetime;
        main.startSpeed = 0.1f; // drift slowly
        main.startSize = 0.015f;
        main.startColor = calmColor;
        main.gravityModifier = -0.01f; // rise very slowly like warm dust
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 500;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = baseEmissionRate;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.04f; // emit from a tight sphere around the hand

        // Fade over lifetime
        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0.0f), new GradientColorKey(Color.white, 1.0f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0.0f, 0.0f), new GradientAlphaKey(1.0f, 0.15f), new GradientAlphaKey(1.0f, 0.7f), new GradientAlphaKey(0.0f, 1.0f) }
        );
        colorOverLifetime.color = grad;

        // Shrink over lifetime
        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(0.2f, 1.0f), new Keyframe(1f, 0.0f));
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        // Turbulence/Noise module for premium organic drifting
        var noise = ps.noise;
        noise.enabled = true;
        noise.frequency = 0.5f;
        noise.strengthMultiplier = calmNoiseStrength;
        noise.quality = ParticleSystemNoiseQuality.Low; // Good performance in VR
        noise.scrollSpeed = 0.1f;
        noise.damping = true;

        // Renderer module config
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        if (renderer != null)
        {
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
        }
    }

    private Transform FindAnchorBySubstring(string side)
    {
        // Try searching in active hierarchy
        foreach (var go in GameObject.FindObjectsOfType<GameObject>())
        {
            if (go.name.ToLower().Contains(side) && go.name.ToLower().Contains("anchor"))
            {
                return go.transform;
            }
        }
        return null;
    }
}
