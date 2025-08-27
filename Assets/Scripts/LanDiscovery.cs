using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class LanDiscovery : MonoBehaviour
{
    [Header("Beacon")]
    public int beaconPort = 5001;

    [Header("Destino")]
    public BpmClient bpmClient;          // arraste no Inspector
    public TMP_Text statusTextOptional;  // opcional, para mensagens de debug

    private Thread listenerThread;
    private volatile bool running;
    private volatile string lastIp;
    private volatile int lastPort;
    private volatile bool hasEndpoint;

    void Start()
    {
        running = true;
        listenerThread = new Thread(ListenLoop) { IsBackground = true };
        listenerThread.Start();
        SetStatus("Procurando servidor na LAN…");
    }

    void OnDestroy()
    {
        running = false;
        try { listenerThread?.Join(200); } catch { }
    }

    void Update()
    {
        // Quando a thread de rede descobriu algo novo, configuramos o cliente no thread principal
        if (hasEndpoint && bpmClient != null)
        {
            hasEndpoint = false;
            bpmClient.SetServer(lastIp, lastPort);
            SetStatus($"Servidor encontrado: {lastIp}:{lastPort}");
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

                // Estrutura esperada: {"service":"coospo-hw9","ip":"X.X.X.X","port_http":5000,"path_bpm":"/bpm", ...}
                var beacon = JsonUtility.FromJson<BeaconPayload>(json);
                if (beacon != null && beacon.service == "coospo-hw9" && !string.IsNullOrEmpty(beacon.ip) && beacon.port_http > 0)
                {
                    lastIp = beacon.ip;
                    lastPort = beacon.port_http;
                    hasEndpoint = true;
                }
            }
        }
        catch (Exception e)
        {
            SetStatus("Discovery erro: " + e.Message);
        }
        finally
        {
            try { udp?.Close(); } catch { }
        }
    }

    void SetStatus(string msg)
    {
        if (statusTextOptional != null) statusTextOptional.text = msg;
        Debug.Log("[LanDiscovery] " + msg);
    }

    [Serializable]
    private class BeaconPayload
    {
        public string service;
        public string ip;
        public int port_http;
        public string path_bpm;
        public string nonce;
    }
}
