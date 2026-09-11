import asyncio
import json
import socket
import sqlite3
import threading
import time
import uuid
import ipaddress
import os
from datetime import datetime
from typing import List, Optional

from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import FileResponse
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel

# =========================
# Configurações
# =========================
HTTP_PORT = 5002
BEACON_PORT = 5002
BEACON_INTERVAL = 1.0  # segundos
DB_PATH = "database.db"

# =========================
# Banco de Dados
# =========================
def init_db():
    conn = sqlite3.connect(DB_PATH)
    c = conn.cursor()
    c.execute('''
        CREATE TABLE IF NOT EXISTS sessions (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            user_name TEXT NOT NULL,
            start_time DATETIME NOT NULL,
            end_time DATETIME,
            status TEXT DEFAULT 'active'
        )
    ''')
    c.execute('''
        CREATE TABLE IF NOT EXISTS sensor_data (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            session_id INTEGER NOT NULL,
            timestamp DATETIME NOT NULL,
            bpm REAL,
            hrv REAL,
            FOREIGN KEY(session_id) REFERENCES sessions(id)
        )
    ''')
    conn.commit()
    conn.close()

init_db()

# =========================
# FastAPI
# =========================
app = FastAPI(title="MetaQuest Data Collection API", version="1.0.0")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)

# Servindo os arquivos estáticos (Frontend)
app.mount("/static", StaticFiles(directory="static"), name="static")

@app.get("/")
def index():
    return FileResponse("static/index.html")

# --- Modelos Pydantic ---
class SessionCreate(BaseModel):
    user_name: str

class SensorData(BaseModel):
    session_id: Optional[int] = None
    bpm: Optional[float] = None
    hrv: Optional[float] = None

# --- Endpoints da API ---
@app.post("/api/sessions")
def create_session(session: SessionCreate):
    conn = sqlite3.connect(DB_PATH)
    c = conn.cursor()
    now = datetime.now().isoformat()
    # Fecha qualquer sessão ativa primeiro (segurança)
    c.execute("UPDATE sessions SET status = 'completed', end_time = ? WHERE status = 'active'", (now,))
    
    c.execute("INSERT INTO sessions (user_name, start_time, status) VALUES (?, ?, 'active')", 
              (session.user_name, now))
    session_id = c.lastrowid
    conn.commit()
    conn.close()
    return {"session_id": session_id, "status": "active"}

@app.put("/api/sessions/stop")
def stop_session():
    conn = sqlite3.connect(DB_PATH)
    c = conn.cursor()
    now = datetime.now().isoformat()
    c.execute("UPDATE sessions SET status = 'completed', end_time = ? WHERE status = 'active'", (now,))
    conn.commit()
    conn.close()
    return {"status": "completed"}

@app.get("/api/sessions")
def get_sessions():
    conn = sqlite3.connect(DB_PATH)
    c = conn.cursor()
    c.execute("SELECT id, user_name, start_time, end_time, status FROM sessions ORDER BY start_time DESC")
    rows = c.fetchall()
    conn.close()
    return [
        {"id": r[0], "user_name": r[1], "start_time": r[2], "end_time": r[3], "status": r[4]}
        for r in rows
    ]

@app.get("/api/sessions/active")
def get_active_session():
    conn = sqlite3.connect(DB_PATH)
    c = conn.cursor()
    c.execute("SELECT id, user_name, start_time, end_time, status FROM sessions WHERE status = 'active' ORDER BY start_time DESC LIMIT 1")
    r = c.fetchone()
    conn.close()
    if r:
        return {"id": r[0], "user_name": r[1], "start_time": r[2], "end_time": r[3], "status": r[4]}
    return None

@app.post("/api/data")
def receive_data(data: SensorData):
    conn = sqlite3.connect(DB_PATH)
    c = conn.cursor()
    now = datetime.now().isoformat()
    
    # Resolve active session if none provided
    session_id = data.session_id
    if not session_id:
        c.execute("SELECT id FROM sessions WHERE status = 'active' ORDER BY start_time DESC LIMIT 1")
        row = c.fetchone()
        if not row:
            conn.close()
            raise HTTPException(status_code=400, detail="Nenhuma sessão ativa.")
        session_id = row[0]
    else:
        # Verifica se a sessão está ativa
        c.execute("SELECT status FROM sessions WHERE id = ?", (session_id,))
        row = c.fetchone()
        if not row or row[0] != 'active':
            conn.close()
            raise HTTPException(status_code=400, detail="Sessão inativa ou inexistente.")

    c.execute("INSERT INTO sensor_data (session_id, timestamp, bpm, hrv) VALUES (?, ?, ?, ?)",
              (session_id, now, data.bpm, data.hrv))
    conn.commit()
    conn.close()
    return {"status": "success", "session_id": session_id}

@app.get("/api/sessions/{session_id}/data")
def get_session_data(session_id: int):
    conn = sqlite3.connect(DB_PATH)
    c = conn.cursor()
    c.execute("SELECT timestamp, bpm, hrv FROM sensor_data WHERE session_id = ? ORDER BY timestamp ASC", (session_id,))
    rows = c.fetchall()
    conn.close()
    return [
        {"timestamp": r[0], "bpm": r[1], "hrv": r[2]}
        for r in rows
    ]


# =========================
# Beacon UDP (descoberta)
# =========================
def get_local_ip() -> str:
    ip = "127.0.0.1"
    try:
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.connect(("8.8.8.8", 80))
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

def beacon_loop():
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM, socket.IPPROTO_UDP)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
    sock.settimeout(0.2)

    nid = str(uuid.uuid4())[:8]
    while True:
        ip_local = get_local_ip()
        payload = {
            "service": "coleta-server",
            "ip": ip_local,
            "port_http": HTTP_PORT,
            "path_data": "/api/data",
            "nonce": nid
        }
        data = json.dumps(payload).encode("utf-8")
        try:
            bcast = guess_broadcast(ip_local)
            sock.sendto(data, (bcast, BEACON_PORT))
            sock.sendto(data, ("255.255.255.255", BEACON_PORT))
            # print(f"[beacon] {ip_local} -> {bcast}:{BEACON_PORT} {payload}")
        except Exception as e:
            print("[beacon] erro ao enviar:", e)
        time.sleep(BEACON_INTERVAL)

beacon_thread = threading.Thread(target=beacon_loop, daemon=True)
beacon_thread.start()

if __name__ == "__main__":
    import uvicorn
    uvicorn.run("server:app", host="0.0.0.0", port=HTTP_PORT, reload=True)
