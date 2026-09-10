package com.company.ble;

import android.app.Activity;
import android.bluetooth.BluetoothAdapter;
import android.bluetooth.BluetoothDevice;
import android.bluetooth.BluetoothGatt;
import android.bluetooth.BluetoothGattCallback;
import android.bluetooth.BluetoothGattCharacteristic;
import android.bluetooth.BluetoothGattDescriptor;
import android.bluetooth.BluetoothGattService;
import android.bluetooth.BluetoothManager;
import android.bluetooth.BluetoothProfile;
import android.bluetooth.le.BluetoothLeScanner;
import android.bluetooth.le.ScanCallback;
import android.bluetooth.le.ScanResult;
import android.content.Context;
import android.util.Log;

import com.unity3d.player.UnityPlayer;

import java.util.UUID;
import java.util.LinkedList;

public class QuestBleHeartRate {
    private static final String TAG = "QuestBleHeartRate";

    // UUIDs for Heart Rate Service
    private static final UUID HR_SERVICE_UUID = UUID.fromString("0000180d-0000-1000-8000-00805f9b34fb");
    private static final UUID HR_CHAR_UUID = UUID.fromString("00002a37-0000-1000-8000-00805f9b34fb");
    private static final UUID CCCD_UUID = UUID.fromString("00002902-0000-1000-8000-00805f9b34fb");

    private Activity activity;
    private BluetoothAdapter bluetoothAdapter;
    private BluetoothLeScanner bluetoothLeScanner;
    private BluetoothGatt bluetoothGatt;

    private String targetMac;
    private boolean isScanning = false;

    public QuestBleHeartRate() {
        activity = UnityPlayer.currentActivity;
        BluetoothManager bluetoothManager = (BluetoothManager) activity.getSystemService(Context.BLUETOOTH_SERVICE);
        if (bluetoothManager != null) {
            bluetoothAdapter = bluetoothManager.getAdapter();
        }
    }

    public void startScan(String mac) {
        if (bluetoothAdapter == null || !bluetoothAdapter.isEnabled()) {
            sendLog("Bluetooth not enabled or not supported.");
            return;
        }
        
        targetMac = mac;
        bluetoothLeScanner = bluetoothAdapter.getBluetoothLeScanner();
        if (bluetoothLeScanner == null) {
            sendLog("Could not get BLE Scanner.");
            return;
        }

        if (isScanning) return;
        
        isScanning = true;
        sendLog("Starting BLE scan for " + (targetMac != null && !targetMac.isEmpty() ? targetMac : "COOSPO"));
        
        try {
            bluetoothLeScanner.startScan(scanCallback);
        } catch(SecurityException e) {
            sendLog("SecurityException: Missing permissions");
        } catch(Exception e) {
            sendLog("Error starting scan: " + e.getMessage());
        }
    }

    public void stopScan() {
        if (bluetoothLeScanner != null && isScanning) {
            try {
                bluetoothLeScanner.stopScan(scanCallback);
            } catch(SecurityException e) {
                // ignore
            }
            isScanning = false;
            sendLog("Stopped BLE scan.");
        }
    }

    public void disconnect() {
        if (bluetoothGatt != null) {
            try {
                bluetoothGatt.disconnect();
                bluetoothGatt.close();
            } catch(SecurityException e) {}
            bluetoothGatt = null;
        }
    }

    private void sendLog(String msg) {
        Log.d(TAG, msg);
        UnityPlayer.UnitySendMessage("QuestBleManager", "OnLog", msg);
    }

    private void sendBpm(int bpm) {
        UnityPlayer.UnitySendMessage("QuestBleManager", "OnBpmReceived", String.valueOf(bpm));
    }

    private void sendRmssd(float rmssd) {
        UnityPlayer.UnitySendMessage("QuestBleManager", "OnRmssdReceived", String.valueOf(rmssd));
    }
    
    // RMSSD Calculation state
    private static final int MAX_RR_WINDOW = 30;
    private LinkedList<Float> rrIntervals = new LinkedList<>();

    private void addRrInterval(float rrMs) {
        rrIntervals.add(rrMs);
        if (rrIntervals.size() > MAX_RR_WINDOW) {
            rrIntervals.removeFirst();
        }

        if (rrIntervals.size() >= 2) {
            float sumSquaredDiffs = 0;
            for (int i = 1; i < rrIntervals.size(); i++) {
                float diff = rrIntervals.get(i) - rrIntervals.get(i - 1);
                sumSquaredDiffs += (diff * diff);
            }
            float meanSquaredDiffs = sumSquaredDiffs / (rrIntervals.size() - 1);
            float rmssd = (float) Math.sqrt(meanSquaredDiffs);
            sendRmssd(rmssd);
        }
    }
    
    private void sendStatus(String status) {
        UnityPlayer.UnitySendMessage("QuestBleManager", "OnStatusChanged", status);
    }

    private ScanCallback scanCallback = new ScanCallback() {
        @Override
        public void onScanResult(int callbackType, ScanResult result) {
            BluetoothDevice device = result.getDevice();
            if (device == null) return;
            
            try {
                String name = device.getName();
                String address = device.getAddress();
                
                boolean match = false;
                if (targetMac != null && !targetMac.isEmpty()) {
                    if (targetMac.equalsIgnoreCase(address)) {
                        match = true;
                    }
                } else if (name != null && name.toUpperCase().contains("COOSPO")) {
                    match = true;
                }
                
                if (match) {
                    sendLog("Device found! Connecting to " + address);
                    stopScan();
                    bluetoothGatt = device.connectGatt(activity, false, gattCallback);
                }
            } catch (SecurityException e) {
                sendLog("SecurityException in scan result");
            }
        }

        @Override
        public void onScanFailed(int errorCode) {
            sendLog("Scan failed with error: " + errorCode);
        }
    };

    private BluetoothGattCallback gattCallback = new BluetoothGattCallback() {
        @Override
        public void onConnectionStateChange(BluetoothGatt gatt, int status, int newState) {
            if (newState == BluetoothProfile.STATE_CONNECTED) {
                sendLog("Connected to GATT server.");
                sendStatus("Connected");
                try {
                    sendLog("Attempting to start service discovery...");
                    gatt.discoverServices();
                } catch(SecurityException e) {}
            } else if (newState == BluetoothProfile.STATE_DISCONNECTED) {
                sendLog("Disconnected from GATT server.");
                sendStatus("Disconnected");
                sendBpm(-1); // Indicator for disconnected
            }
        }

        @Override
        public void onServicesDiscovered(BluetoothGatt gatt, int status) {
            if (status == BluetoothGatt.GATT_SUCCESS) {
                sendLog("Services discovered.");
                BluetoothGattService hrService = gatt.getService(HR_SERVICE_UUID);
                if (hrService != null) {
                    BluetoothGattCharacteristic hrChar = hrService.getCharacteristic(HR_CHAR_UUID);
                    if (hrChar != null) {
                        try {
                            gatt.setCharacteristicNotification(hrChar, true);
                            BluetoothGattDescriptor descriptor = hrChar.getDescriptor(CCCD_UUID);
                            if (descriptor != null) {
                                descriptor.setValue(BluetoothGattDescriptor.ENABLE_NOTIFICATION_VALUE);
                                gatt.writeDescriptor(descriptor);
                                sendLog("HR Notifications enabled.");
                            }
                        } catch(SecurityException e) {}
                    } else {
                        sendLog("HR Characteristic not found.");
                    }
                } else {
                    sendLog("HR Service not found.");
                }
            } else {
                sendLog("onServicesDiscovered received: " + status);
            }
        }

        @Override
        public void onCharacteristicChanged(BluetoothGatt gatt, BluetoothGattCharacteristic characteristic) {
            if (HR_CHAR_UUID.equals(characteristic.getUuid())) {
                byte[] data = characteristic.getValue();
                if (data != null && data.length > 0) {
                    int flags = data[0];
                    boolean hrFormat16 = (flags & 0x01) != 0;
                    boolean eePresent = (flags & 0x08) != 0;
                    boolean rrPresent = (flags & 0x10) != 0;

                    int offset = 1;
                    int format = hrFormat16 ? BluetoothGattCharacteristic.FORMAT_UINT16 : BluetoothGattCharacteristic.FORMAT_UINT8;
                    
                    if (data.length > offset) {
                        final int heartRate = characteristic.getIntValue(format, offset);
                        sendBpm(heartRate);
                        offset += hrFormat16 ? 2 : 1;
                    }

                    if (eePresent) {
                        offset += 2; // skip energy expended
                    }

                    if (rrPresent) {
                        while (offset + 1 < data.length) {
                            int rrValue = characteristic.getIntValue(BluetoothGattCharacteristic.FORMAT_UINT16, offset);
                            offset += 2;
                            
                            // RR interval is in units of 1/1024 seconds
                            float rrMs = (rrValue / 1024.0f) * 1000.0f;
                            addRrInterval(rrMs);
                        }
                    }
                }
            }
        }
    };
}
