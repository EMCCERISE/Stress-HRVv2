using System;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
[DisallowMultipleComponent]
public class BinauralAudioGenerator : MonoBehaviour
{
    [Header("Bpm Connection")]
    [Tooltip("Source client for BPM updates. If null, will run at the default target beat frequency.")]
    public BpmClient bpmSource;

    [Header("Binaural Settings")]
    [Range(20f, 600f)]
    [Tooltip("Base pitch frequency (carrier) played in the background. Lower is warmer, higher is clearer. Note: 150Hz is very low bass, you may need 300Hz-400Hz to hear it on built-in speakers.")]
    public float carrierFrequency = 150f;

    [Header("Testing / Debugging")]
    [Tooltip("Plays a loud standard 440Hz test tone (A4) in mono to check if the Unity audio engine output is working at all.")]
    public bool playTestTone = false;

    [Range(0.5f, 30f)]
    [Tooltip("Current active beat frequency (difference between left and right channels).")]
    public float currentBeatFrequency = 6f;

    [Range(0f, 1f)]
    [Tooltip("Volume of the synthesized binaural beats.")]
    public float volume = 0.15f;

    [Header("Biofeedback Mapping")]
    [Tooltip("Enables auto-adjusting the beat frequency based on the BPM.")]
    public bool enableBiofeedback = true;

    [Tooltip("Heart rate representing deep relaxation.")]
    public float bpmMin = 60f;
    [Tooltip("Heart rate representing heightened activity or stress.")]
    public float bpmMax = 100f;

    [Tooltip("Binaural beat frequency at the lowest BPM (e.g. Theta/Delta waves for deep calm).")]
    public float beatFreqMin = 4f;
    [Tooltip("Binaural beat frequency at the highest BPM (e.g. Alpha/Beta waves).")]
    public float beatFreqMax = 12f;

    [Tooltip("How fast the beat frequency adjusts to the target BPM (lower = smoother transition).")]
    [Range(0.1f, 10f)]
    public float smoothingSpeed = 1f;

    // Internal state
    private AudioSource _audioSource;
    private double _sampleRate;
    private double _phaseLeft;
    private double _phaseRight;
    private float _targetBeatFrequency;

    void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
        
        // Ensure AudioSource is enabled and volume is audible
        _audioSource.volume = 1f;
        _audioSource.mute = false;
        
        // Unity requires a clip to be assigned and playing for OnAudioFilterRead to be called.
        // If there's no clip, we generate a silent dummy clip in memory.
        if (_audioSource.clip == null)
        {
            _audioSource.clip = AudioClip.Create("SilentDummy", 44100, 1, 44100, false);
        }

        // AudioSource configuration for programmatic synthesis
        _audioSource.playOnAwake = true;
        _audioSource.loop = true;
        
        // Set spatialBlend to 0 (2D) because binaural beats REQUIRE direct channel-to-ear separation.
        // Spatial 3D audio will mix channels together in 3D space, which destroys the binaural effect.
        _audioSource.spatialBlend = 0f; 

        _sampleRate = AudioSettings.outputSampleRate;
        if (_sampleRate <= 0) _sampleRate = 44100;
        
        _targetBeatFrequency = currentBeatFrequency;
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
        
        // Ensure AudioSource is playing to trigger OnAudioFilterRead
        if (!_audioSource.isPlaying)
        {
            _audioSource.Play();
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
        // Smoothly interpolate towards the target beat frequency to avoid click/pop sounds
        if (Mathf.Abs(currentBeatFrequency - _targetBeatFrequency) > 0.01f)
        {
            currentBeatFrequency = Mathf.MoveTowards(
                currentBeatFrequency, 
                _targetBeatFrequency, 
                smoothingSpeed * Time.deltaTime
            );
        }
    }

    private void OnBpmChanged(int? bpm)
    {
        if (!enableBiofeedback) return;

        int activeBpm = bpm ?? 60;

        // Map current BPM to the target beat frequency range
        float t = Mathf.InverseLerp(bpmMin, bpmMax, activeBpm);
        _targetBeatFrequency = Mathf.Lerp(beatFreqMin, beatFreqMax, t);
    }

    // Called on the audio thread by Unity
    void OnAudioFilterRead(float[] data, int channels)
    {
        if (volume <= 0f || channels <= 0) return;

        double sampleRate = _sampleRate > 0 ? _sampleRate : 44100.0;

        if (playTestTone)
        {
            double testFreq = 440.0; // standard A4 note (highly audible)
            double increment = testFreq * 2.0 * Math.PI / sampleRate;
            for (int i = 0; i < data.Length; i += channels)
            {
                float val = (float)(Math.Sin(_phaseLeft) * 0.35f); // 35% volume
                _phaseLeft += increment;
                if (_phaseLeft > 2.0 * Math.PI) _phaseLeft -= 2.0 * Math.PI;

                for (int c = 0; c < channels; c++)
                {
                    if (i + c < data.Length)
                    {
                        data[i + c] += val;
                    }
                }
            }
            return;
        }

        // Calculate left and right frequencies around the carrier frequency
        double freqLeft = carrierFrequency - (currentBeatFrequency / 2.0f);
        double freqRight = carrierFrequency + (currentBeatFrequency / 2.0f);

        double incrementLeft = freqLeft * 2.0 * Math.PI / sampleRate;
        double incrementRight = freqRight * 2.0 * Math.PI / sampleRate;

        for (int i = 0; i < data.Length; i += channels)
        {
            // Synthesize stereo wave
            float leftVal = (float)(Math.Sin(_phaseLeft) * volume);
            float rightVal = (float)(Math.Sin(_phaseRight) * volume);

            _phaseLeft += incrementLeft;
            _phaseRight += incrementRight;

            // Keep phase bounded
            if (_phaseLeft > 2.0 * Math.PI) _phaseLeft -= 2.0 * Math.PI;
            if (_phaseRight > 2.0 * Math.PI) _phaseRight -= 2.0 * Math.PI;

            if (channels >= 2)
            {
                // Overlay/add our synthesized wave onto the existing AudioSource data
                // This preserves any ambient clip playing on the same AudioSource
                data[i] += leftVal;       // Left channel
                data[i + 1] += rightVal;   // Right channel
            }
            else
            {
                // Fallback for mono setup (though binaural beat effect is lost)
                data[i] += leftVal;
            }
        }
    }
}
