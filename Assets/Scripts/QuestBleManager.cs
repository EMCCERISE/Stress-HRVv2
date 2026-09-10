using System;
using System.Collections;
using UnityEngine;
using TMPro;

#if UNITY_ANDROID
using UnityEngine.Android;
#endif

[DisallowMultipleComponent]
public class QuestBleManager : MonoBehaviour
{
    [Header("BLE Configuration")]
    [Tooltip("MAC Address of the Coospo HW9. If empty, it will search for 'COOSPO'.")]
    public string targetMacAddress = "EA:30:3E:50:BF:21";

    [Header("UI (Optional)")]
    public TMP_Text bpmText;
    public TMP_Text statusText;

    [Header("Fallback Settings")]
    public bool useFallbackOnDisconnect = true;
    public int fallbackBpmValue = 60;

    // Public event identical to BpmClient to plug into HeartRateLightController
    public event Action<int?> OnBpmChanged;
    public int? LastBpm { get; private set; }

    // Public event for RMSSD (HRV)
    public event Action<float?> OnRmssdChanged;
    public float? LastRmssd { get; private set; }

#if UNITY_ANDROID && !UNITY_EDITOR
    private AndroidJavaObject blePlugin;
#endif

    void Start()
    {
        // CRÍTICO: Este GameObject PRECISA se chamar QuestBleManager para receber mensagens do Java
        gameObject.name = "QuestBleManager";

        if (useFallbackOnDisconnect)
        {
            SetBpm(fallbackBpmValue, true);
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        RequestPermissionsAndStart();
#else
        SetStatus("BLE nativo só funciona no build Android/Quest.");
        Debug.LogWarning("QuestBleManager will only run on Android devices.");
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private void RequestPermissionsAndStart()
    {
        string[] permissions = new string[]
        {
            "android.permission.BLUETOOTH_SCAN",
            "android.permission.BLUETOOTH_CONNECT",
            "android.permission.ACCESS_FINE_LOCATION",
            "android.permission.ACCESS_COARSE_LOCATION"
        };

        bool allGranted = true;
        foreach (var p in permissions)
        {
            if (!Permission.HasUserAuthorizedPermission(p))
            {
                allGranted = false;
                break;
            }
        }

        if (!allGranted)
        {
            SetStatus("Requesting permissions...");
            Permission.RequestUserPermissions(permissions);
            StartCoroutine(WaitForPermissions());
        }
        else
        {
            StartBle();
        }
    }

    private IEnumerator WaitForPermissions()
    {
        // Aguarda o usuário responder ao diálogo do Android
        yield return new WaitForSeconds(2.0f);
        while (true)
        {
            // O Unity pode pausar durante o diálogo. Continuamos checando a principal permissão de scan ou localização.
            if (Permission.HasUserAuthorizedPermission("android.permission.ACCESS_FINE_LOCATION") || 
                Permission.HasUserAuthorizedPermission("android.permission.BLUETOOTH_SCAN"))
            {
                SetStatus("Permissions granted.");
                StartBle();
                yield break;
            }
            yield return new WaitForSeconds(1.0f);
        }
    }

    private void StartBle()
    {
        SetStatus("Inicializando Plugin BLE...");
        try
        {
            // Instancia a classe Java que criamos
            blePlugin = new AndroidJavaObject("com.company.ble.QuestBleHeartRate");
            // Chama o método startScan, passando o MAC
            blePlugin.Call("startScan", targetMacAddress);
        }
        catch (Exception e)
        {
            SetStatus("Error loading plugin: " + e.Message);
        }
    }

    private void OnDestroy()
    {
        if (blePlugin != null)
        {
            blePlugin.Call("disconnect");
            blePlugin.Call("stopScan");
            blePlugin.Dispose();
            blePlugin = null;
        }
    }
#endif

    // ---- Métodos chamados nativamente pelo Java via UnitySendMessage ----

    public void OnLog(string message)
    {
        Debug.Log("[QuestBLE] " + message);
        SetStatus(message); // Útil para ver os logs do Java direto na tela do Quest
    }

    public void OnStatusChanged(string status)
    {
        SetStatus("BLE Status: " + status);
        if (status == "Disconnected" && useFallbackOnDisconnect)
        {
            SetBpm(fallbackBpmValue, true);
        }
    }

    public void OnBpmReceived(string bpmString)
    {
        if (int.TryParse(bpmString, out int bpm))
        {
            if (bpm == -1) 
            {
                // Enviado pelo Java quando desconecta
                if (useFallbackOnDisconnect)
                {
                    SetBpm(fallbackBpmValue, true);
                }
                else
                {
                    SetBpm(null, false);
                }
            }
            else
            {
                SetBpm(bpm, false);
            }
        }
    }

    public void OnRmssdReceived(string rmssdString)
    {
        // Try parsing float with InvariantCulture since Java's String.valueOf(float) uses '.'
        if (float.TryParse(rmssdString, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float rmssd))
        {
            LastRmssd = rmssd;
            OnRmssdChanged?.Invoke(LastRmssd);
        }
    }

    // --------------------------------------------------------

    private void SetBpm(int? bpm, bool isFallback)
    {
        LastBpm = bpm;
        OnBpmChanged?.Invoke(LastBpm);

        if (bpmText != null)
        {
            if (bpm.HasValue)
            {
                bpmText.text = bpm.Value + (isFallback ? " bpm (fb)" : " bpm");
            }
            else
            {
                bpmText.text = "-- bpm";
            }
        }
    }

    private void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
    }
}
