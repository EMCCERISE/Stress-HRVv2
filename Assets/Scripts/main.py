
# main.py — Coospo HW9 -> FastAPI + UDP Beacon (LAN discovery)
# - BLE via Bleak: lê HR (0x2A37) e RR
# - Endpoints: /, /bpm, /hr, /rr, /stats, /debug
# - Beacon UDP: envia anúncio JSON a cada 1s na porta 5001 (broadcast)
#
# Execução:
#   pip install fastapi uvicorn[standard] bleak
#   python -m uvicorn main:app --host 0.0.0.0 --port 5000
#
# Observação: mantenha o dispositivo Coospo HW9 no peito (eletrodos úmidos).

import asyncio
import json
import socket
import threading
import time
import uuid
import ipaddress
from datetime import datetime
from typing import List, Tuple, Optional
from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
try:
    from bleak import BleakClient, BleakScanner
except Exception as e:
    raise SystemExit("Bleak não instalado. Rode: pip install bleak") from e


# =========================
# Configurações
# =========================
HR_UUID = "00002a37-0000-1000-8000-00805f9b34fb"  # Heart Rate Measurement
# Substitua pelo MAC do seu HW9. Em alguns SOs, resolvable-random muda: prefira scan por nome se necessário.
DEVICE_ADDRESS = "EA:30:3E:50:BF:21"

HTTP_PORT = 5000
BEACON_PORT = 5001
BEACON_INTERVAL = 1.0  # segundos
MAX_POINTS = 500  # tamanho do buffer de RR

# =========================
# Estado global
# =========================
rr_data: List[Tuple[datetime, float]] = []  # (timestamp, rr_ms)
latest_bpm: Optional[int] = None

# debug
last_raw_hex: Optional[str] = None
last_flags: Optional[int] = None

# =========================
# FastAPI
# =========================
app = FastAPI(title="Coospo HW9 API", version="1.0.0")
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)

@app.get("/")
def root():
    return {"status": "ok", "service": "coospo-hw9", "message": "Servidor ativo"}

@app.get("/bpm")
def get_bpm():
    return {"bpm": latest_bpm}

@app.get("/hr")
def get_hr():
    return {"bpm": latest_bpm}

@app.get("/rr")
def get_rr(limit: int = 100):
    sl = rr_data[-limit:]
    return [{"timestamp": t.isoformat(), "rr_ms": rr} for t, rr in sl]

@app.get("/stats")
def get_stats():
    n = len(rr_data)
    if n < 2:
        return {"count": n, "error": "Dados insuficientes"}
    vals = [rr for _, rr in rr_data]
    mean = sum(vals) / n
    var = sum((v - mean) ** 2 for v in vals) / n
    std = var ** 0.5
    diffs = [(vals[i+1] - vals[i]) for i in range(n-1)]
    rmssd = (sum(d*d for d in diffs) / (n-1)) ** 0.5
    return {"count": n, "mean_rr_ms": mean, "std_rr_ms": std, "rmssd": rmssd}

@app.get("/debug")
def debug():
    return {
        "latest_bpm": latest_bpm,
        "rr_count": len(rr_data),
        "last_raw_hex": last_raw_hex,
        "last_flags": last_flags,
        "hr_uint16": (last_flags & 0x01) if last_flags is not None else None,
        "rr_present": bool(last_flags & 0x10) if last_flags is not None else None,
    }

# ===== Endpoints auxiliares =====
@app.get("/ble/state")
def ble_state():
    return {"connected": ble_connected, "device_address": DEVICE_ADDRESS}

@app.post("/ble/reconnect")
def ble_force_reconnect():
    ble_control["force_reconnect"] = True
    return {"ok": True, "action": "force_reconnect"}
# =========================
# BLE (Coospo HW9) — reconexão automática
# =========================

# Hints para descobrir o dispositivo via scan quando o endereço mudar (resolvable random)
BLE_NAME_HINTS = ["COOSPO", "HW9", "COOSPO HW9"]
RETRY_BASE = 3.0

ble_connected = False
ble_control = {"force_reconnect": False}  # setado pelo endpoint para forçar reconexão

async def find_device_address(timeout: float = 6.0) -> Optional[str]:
    """Tenta descobrir o endereço atual pelo nome/serviço (útil se o MAC muda a cada pareamento)."""
    try:
        devices = await BleakScanner.discover(timeout=timeout)
        for d in devices:
            name = (d.name or "").upper()
            if any(h in name for h in BLE_NAME_HINTS):
                print(f"[BLE] Descoberto por scan: {name} @ {d.address}")
                return d.address
    except Exception as e:
        print("[BLE] scan erro:", e)
    return None

async def ble_manager():
    """Loop principal: conecta, inicia notify, monitora e reconecta se cair."""
    global ble_connected, DEVICE_ADDRESS
    backoff = RETRY_BASE

    while True:
        client = None
        try:
            addr = DEVICE_ADDRESS
            # Se não tiver endereço definido ou conexão falhar repetidamente, tente descobrir por scan
            if not addr or addr.startswith("??"):
                cand = await find_device_address()
                if cand:
                    DEVICE_ADDRESS = addr = cand

            if not addr:
                print("[BLE] Sem endereço para conectar; tentando novamente…")
                await asyncio.sleep(backoff)
                backoff = min(backoff * 1.5, 30.0)
                continue

            client = BleakClient(addr)
            client.set_disconnected_callback(lambda c: print("[BLE] desconectado (callback)"))

            print(f"[BLE] Conectando a {addr} …")
            await client.connect(timeout=10.0)
            print("[BLE] Conectado")
            ble_connected = True

            await client.start_notify(HR_UUID, handle_heart_rate)
            print("[BLE] Notificações 0x2A37 iniciadas")

            backoff = RETRY_BASE  # sucesso → zera backoff

            # Monitor de conexão
            while True:
                # Força reconexão sob demanda (endpoint)
                if ble_control["force_reconnect"]:
                    print("[BLE] force_reconnect solicitado")
                    ble_control["force_reconnect"] = False
                    try:
                        await client.disconnect()
                    except Exception:
                        pass
                    break

                # Se desconectou “silenciosamente”, sai para reconectar
                if not await client.is_connected():
                    print("[BLE] conexão perdida (poll)")
                    break

                await asyncio.sleep(1.0)

        except Exception as e:
            print("[BLE] erro:", e)
        finally:
            ble_connected = False
            if client is not None:
                try:
                    await client.disconnect()
                except Exception:
                    pass
            print(f"[BLE] Reconnect em {backoff:.1f}s…")
            await asyncio.sleep(backoff)
            # tenta descobrir novo endereço se o MAC tiver mudado
            cand = await find_device_address(timeout=4.0)
            if cand:
                DEVICE_ADDRESS = cand
            backoff = min(backoff * 1.5, 30.0)

def start_ble_thread():
    loop = asyncio.new_event_loop()
    asyncio.set_event_loop(loop)
    loop.run_until_complete(ble_manager())



# =========================
# Beacon UDP (descoberta)
# =========================
def get_local_ip() -> str:
    ip = "127.0.0.1"
    try:
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.connect(("8.8.8.8", 80))  # força SO a escolher a rota/iface
        ip = s.getsockname()[0]
    except Exception as e:
        print("[beacon] get_local_ip erro:", e)
    finally:
        try:
            s.close()
        except:
            pass
    return ip

def guess_broadcast(ip: str) -> str:
    try:
        net = ipaddress.ip_network(ip + "/24", strict=False)
        return str(net.broadcast_address)
    except Exception:
        return "255.255.255.255"

def beacon_loop(http_port=HTTP_PORT, ws_port=HTTP_PORT):
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM, socket.IPPROTO_UDP)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
    sock.settimeout(0.2)

    nid = str(uuid.uuid4())[:8]
    while True:
        ip_local = get_local_ip()
        payload = {
            "service": "coospo-hw9",
            "ip": ip_local,
            "port_http": http_port,
            "port_ws": ws_port,
            "path_bpm": "/bpm",
            "path_ws": "/ws",
            "nonce": nid
        }
        data = json.dumps(payload).encode("utf-8")
        try:
            bcast = guess_broadcast(ip_local)
            sock.sendto(data, (bcast, BEACON_PORT))
            sock.sendto(data, ("255.255.255.255", BEACON_PORT))
            print(f"[beacon] {ip_local} -> {bcast},{'255.255.255.255'}:{BEACON_PORT} {payload}")
        except Exception as e:
            print("[beacon] erro ao enviar:", e)
        time.sleep(BEACON_INTERVAL)

# =========================
# BLE (Coospo HW9)
# =========================
def handle_heart_rate(_, data: bytearray):
    """
    Callback robusto (sem asyncio): atualiza latest_bpm e rr_data.
    Fallback: se não veio HR, estima a partir do último RR.
    """
    global latest_bpm, last_raw_hex, last_flags

    raw = bytes(data)
    last_raw_hex = raw.hex(" ")
    flags = raw[0]
    last_flags = flags

    hr_uint16 = flags & 0x01
    rr_present = flags & 0x10
    idx = 1

    # HR
    hr = None
    try:
        if hr_uint16:
            if idx + 1 < len(raw):
                hr = int.from_bytes(raw[idx:idx+2], "little")
                idx += 2
        else:
            if idx < len(raw):
                hr = raw[idx]
                idx += 1
    except Exception:
        hr = None

    if hr is not None:
        latest_bpm = int(hr)
        print("BPM: " + str(latest_bpm))

    # RR list
    now = datetime.now()
    if rr_present:
        while idx + 1 < len(raw):
            rr_s = int.from_bytes(raw[idx:idx+2], "little") / 1024.0
            rr_ms = rr_s * 1000.0
            rr_data.append((now, rr_ms))
            if len(rr_data) > MAX_POINTS:
                del rr_data[0:(len(rr_data) - MAX_POINTS)]
            idx += 2

    # Fallback BPM por RR
    if latest_bpm is None and rr_present:
        for i in range(len(rr_data) - 1, -1, -1):
            rr_ms = rr_data[i][1]
            if rr_ms > 0:
                latest_bpm = int(round(60000.0 / rr_ms))
                break

async def ble_loop():
    print(f"[BLE] Conectando ao HW9 em {DEVICE_ADDRESS} …")
    try:
        async with BleakClient(DEVICE_ADDRESS) as client:
            ok = await client.is_connected()
            print("[BLE] Conectado?", ok)
            # Inicia notificações
            await client.start_notify(HR_UUID, handle_heart_rate)
            print("[BLE] Notificações 0x2A37 iniciadas. Coletando…")
            while True:
                await asyncio.sleep(1)
    except Exception as e:
        print("[BLE] erro no loop:", e)
        # Tenta reconectar após um pequeno atraso
        await asyncio.sleep(3.0)
        await ble_loop()

#def start_ble_thread():
#    loop = asyncio.new_event_loop()
#    asyncio.set_event_loop(loop)
#    loop.run_until_complete(ble_loop())



# =========================
# Bootstrap de threads
# =========================
ble_thread = threading.Thread(target=start_ble_thread, daemon=True)
ble_thread.start()

beacon_thread = threading.Thread(target=beacon_loop, kwargs={"http_port": HTTP_PORT, "ws_port": HTTP_PORT}, daemon=True)
beacon_thread.start()


#python -m uvicorn main:app --host 0.0.0.0 --port 5000 --workers 1