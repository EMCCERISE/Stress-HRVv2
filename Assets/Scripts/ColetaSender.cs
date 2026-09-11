using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;

[DisallowMultipleComponent]
public class ColetaSender : MonoBehaviour
{
    [Header("Configuração de Descoberta")]
    public int beaconPort = 5002;
    public string serviceName = "coleta-server";

    [Header("Referências")]
    [Tooltip("Arraste o QuestBleManager aqui para ler os eventos de BPM/HRV.")]
    public QuestBleManager bleManager;
    public TMP_Text statusTextOptional;

    private Thread listenerThread;
    private volatile bool running;
    
    // Server Endpoints
    private volatile string serverUrl = "";
    private volatile bool hasEndpoint;

    // Data Cache
    private int? currentBpm = null;
    private float? currentHrv = null;

    void Start()
    {
        running = true;
        listenerThread = new Thread(ListenLoop) { IsBackground = true };
        listenerThread.Start();
        SetStatus("Procurando servidor de coleta na LAN...");

        if (bleManager != null)
        {
            bleManager.OnBpmChanged += HandleBpmChanged;
            bleManager.OnRmssdChanged += HandleHrvChanged;
        }
        else
        {
            Debug.LogWarning("[ColetaSender] QuestBleManager não está referenciado!");
        }
    }

    void OnDestroy()
    {
        running = false;
        try { listenerThread?.Join(200); } catch { }
        
        if (bleManager != null)
        {
            bleManager.OnBpmChanged -= HandleBpmChanged;
            bleManager.OnRmssdChanged -= HandleHrvChanged;
        }
    }

    void Update()
    {
        if (hasEndpoint)
        {
            hasEndpoint = false;
            SetStatus($"Servidor de Coleta conectado: {serverUrl}");
        }
    }

    private void HandleBpmChanged(int? bpm)
    {
        currentBpm = bpm;
        SendData();
    }

    private void HandleHrvChanged(float? hrv)
    {
        currentHrv = hrv;
        SendData();
    }

    private void SendData()
    {
        if (string.IsNullOrEmpty(serverUrl) || !currentBpm.HasValue) return;

        // Monta o JSON base
        string json = $"{{\"session_id\": 0, \"bpm\": {currentBpm.Value}";
        
        // Formata HRV usando cultura invariante para evitar virgula ao invés de ponto
        if (currentHrv.HasValue)
        {
            json += $", \"hrv\": {currentHrv.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        }
        json += "}";

        StartCoroutine(PostDataCoroutine(serverUrl, json));
    }

    private IEnumerator PostDataCoroutine(string url, string jsonPayload)
    {
        using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.ProtocolError)
            {
                if (req.responseCode == 400) {
                    // Sessão inativa, silenciamos para não floodar
                } else {
                    string errorMsg = $"[ColetaSender] Erro ao enviar: {req.error}";
                    Debug.LogError(errorMsg);
                    SetStatus(errorMsg); // Mostra na tela do Quest
                }
            }
            else 
            {
                // SetStatus($"Enviado: BPM {currentBpm}"); // Descomente para ver no painel se está enviando
            }
        }
    }

    void ListenLoop()
    {
        UdpClient udp = null;
        try
        {
            udp = new UdpClient();
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.ExclusiveAddressUse = false;
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, beaconPort));

            var endpoint = new IPEndPoint(IPAddress.Any, 0);

            while (running)
            {
                if (udp.Available == 0)
                {
                    Thread.Sleep(50);
                    continue;
                }

                byte[] data = udp.Receive(ref endpoint);
                string json = Encoding.UTF8.GetString(data);

                var beacon = JsonUtility.FromJson<BeaconPayload>(json);
                if (beacon != null && beacon.service == serviceName && !string.IsNullOrEmpty(beacon.ip) && beacon.port_http > 0)
                {
                    string newUrl = $"http://{beacon.ip}:{beacon.port_http}{beacon.path_data}";
                    if (serverUrl != newUrl)
                    {
                        serverUrl = newUrl;
                        hasEndpoint = true;
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[ColetaSender] Discovery erro: " + e.Message);
        }
        finally
        {
            try { udp?.Close(); } catch { }
        }
    }

    void SetStatus(string msg)
    {
        if (statusTextOptional != null) statusTextOptional.text = msg;
        Debug.Log("[ColetaSender] " + msg);
    }

    [Serializable]
    private class BeaconPayload
    {
        public string service;
        public string ip;
        public int port_http;
        public string path_data;
        public string nonce;
    }
}
