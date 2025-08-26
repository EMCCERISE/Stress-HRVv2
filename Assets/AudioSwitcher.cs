using System.Collections;
using UnityEngine;

public class AudioSwitcher : MonoBehaviour
{
    public AudioSource audioSource;   // se deixar vazio, pega do mesmo GO
    public AudioClip sound1;
    public AudioClip sound2;

    [Header("Opcional: crossfade")]
    public float fadeTime = 0.5f;       // 0 = troca seca; 0.25–0.5 = crossfade suave

    void Awake()
    {
        if (!audioSource) audioSource = GetComponent<AudioSource>();
    }

    public void PlaySound1() => SwitchTo(sound1);
    public void PlaySound2() => SwitchTo(sound2);

    void SwitchTo(AudioClip clip)
    {
        if (!audioSource || !clip) return;

        if (fadeTime <= 0f)
        {
            audioSource.Stop();
            audioSource.clip = clip;
            audioSource.Play();
        }
        else
        {
            StopAllCoroutines();
            StartCoroutine(FadeTo(clip));
        }
    }

    IEnumerator FadeTo(AudioClip newClip)
    {
        float startVol = audioSource.volume;
        for (float t = 0; t < fadeTime; t += Time.deltaTime)
        {
            audioSource.volume = Mathf.Lerp(startVol, 0f, t / fadeTime);
            yield return null;
        }
        audioSource.Stop();
        audioSource.clip = newClip;
        audioSource.Play();
        for (float t = 0; t < fadeTime; t += Time.deltaTime)
        {
            audioSource.volume = Mathf.Lerp(0f, startVol, t / fadeTime);
            yield return null;
        }
        audioSource.volume = startVol;
    }
}
