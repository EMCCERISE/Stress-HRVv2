using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

[DisallowMultipleComponent]
public class BpmClient : MonoBehaviour
{
    [Header("UI")]
    public TMP_Text bpmText;            // arraste seu TMP aqui
    public TMP_Text smallStatusOptional; // opcional (exibe IP/erros)

    [Header("Polling")]
    public float intervalSeconds = 0.5f;
    public int timeoutSeconds = 3;

    [Header("Fallback Settings")]
    [Tooltip("If true, sends the fallback BPM when the server is unreachable or offline.")]
    public bool useFallbackOnDisconnect = true;
    [Tooltip("The fallback BPM value to send when offline.")]
    public int fallbackBpmValue = 60;

    private string serverIp;
    private int serverPort;
    private string bpmUrl;
    private Coroutine pollingCo;

    // (1) ADICIONE: evento e propriedade
    public event Action<int?> OnBpmChanged;
    public int? LastBpm { get; private set; }

    void Start()
    {
        if (useFallbackOnDisconnect)
        {
            LastBpm = fallbackBpmValue;
            if (bpmText) bpmText.text = fallbackBpmValue + " bpm (fb)";
            SetStatus($"Usando BPM de Fallback inicial: {fallbackBpmValue}");
        }
        else
        {
            if (bpmText) bpmText.text = "-- bpm";
        }
    }

    public void SetServer(string ip, int port)
    {
        serverIp = ip;
        serverPort = port;
        bpmUrl = $"http://{serverIp}:{serverPort}/bpm";

        if (pollingCo != null) StopCoroutine(pollingCo);
        pollingCo = StartCoroutine(PollLoop());

        SetStatus($"Conectando a {serverIp}:{serverPort}...");
    }

    IEnumerator PollLoop()
    {
        var wait = new WaitForSeconds(intervalSeconds);

        while (true)
        {
            if (string.IsNullOrEmpty(bpmUrl))
            {
                yield return wait;
                continue;
            }

            using (UnityWebRequest req = UnityWebRequest.Get(bpmUrl))
            {
                req.timeout = timeoutSeconds;
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    var json = req.downloadHandler.text;
                    int? bpm = TryParseBpm(json);

                    bool isFallbackActive = false;
                    if (!bpm.HasValue && useFallbackOnDisconnect)
                    {
                        bpm = fallbackBpmValue;
                        isFallbackActive = true;
                    }

                    // (2) ADICIONE: guarde e notifique
                    LastBpm = bpm;
                    OnBpmChanged?.Invoke(LastBpm);

                    if (bpm.HasValue && bpm.Value > 0)
                    {
                        if (bpmText) bpmText.text = bpm.Value + (isFallbackActive ? " bpm (fb)" : " bpm");
                        SetStatus($"OK {serverIp}:{serverPort}" + (isFallbackActive ? " (Fallback)" : ""));
                    }
                    else
                    {
                        if (bpmText) bpmText.text = "-- bpm";
                        SetStatus("Sem valor (null)");
                    }
                }
                else
                {
                    // (3) ADICIONE: propaga null ou fallback quando der erro
                    if (useFallbackOnDisconnect)
                    {
                        LastBpm = fallbackBpmValue;
                        OnBpmChanged?.Invoke(LastBpm);

                        if (bpmText) bpmText.text = fallbackBpmValue + " bpm (fb)";
                    }
                    else
                    {
                        LastBpm = null;
                        OnBpmChanged?.Invoke(LastBpm);

                        if (bpmText) bpmText.text = "?? bpm";
                    }
                    
                    SetStatus($"ERROR {serverIp}:{serverPort}: " + req.error);
                }
            }

            yield return wait;
        }
    }

    // Parser simples: aceita {"bpm": 72} ou {"bpm": null}
    int? TryParseBpm(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        if (json.Contains("\"bpm\":null")) return null;

        int idx = json.IndexOf("\"bpm\"");
        if (idx < 0) return null;
        int colon = json.IndexOf(':', idx);
        if (colon < 0) return null;

        int i = colon + 1;
        while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
        int j = i;
        while (j < json.Length && (char.IsDigit(json[j]) || json[j] == '-')) j++;

        if (int.TryParse(json.Substring(i, j - i), out int bpm))
            return bpm;

        return null;
    }

    void SetStatus(string msg)
    {
        if (smallStatusOptional != null) smallStatusOptional.text = msg;
        Debug.Log("[BpmClient] " + msg);
    }
}
