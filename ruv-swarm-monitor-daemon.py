#!/usr/bin/env python3
"""
Ruv-Swarm Monitor Daemon - Background Service
==============================================
Claude Code oturumu başladığında arka planda çalışır.
Named pipe / Unix socket üzerinden veri sağlar.
"""

import json
import subprocess
import sys
import time
import os
import signal
import threading
from datetime import datetime
from http.server import HTTPServer, BaseHTTPRequestHandler
from urllib.parse import urlparse, parse_qs
import queue

# Global data store
monitor_data = {
    'swarm_status': {},
    'agents': [],
    'agent_health': [],
    'tasks': [],
    'system_health': {},
    'system_metrics': {},
    'last_update': None,
    'errors': []
}
data_lock = threading.Lock()
data_queue = queue.Queue()

def run_ruv_swarm_command(cmd_args):
    """ruv-swarm CLI komutu çalıştır (PowerShell script)."""
    try:
        ps_cmd = ['powershell', '-NoProfile', '-Command', 'ruv-swarm'] + cmd_args
        result = subprocess.run(
            ps_cmd,
            capture_output=True,
            text=True,
            timeout=15
        )
        if result.returncode == 0:
            try:
                return json.loads(result.stdout)
            except json.JSONDecodeError:
                return {"raw": result.stdout.strip()}
        else:
            return {"error": result.stderr.strip()}
    except subprocess.TimeoutExpired:
        return {"error": "Timeout"}
    except Exception as e:
        return {"error": str(e)}

def collect_data():
    """Tüm verileri topla."""
    global monitor_data

    data = {
        'timestamp': datetime.now().isoformat(),
        'swarm_status': run_ruv_swarm_command(['status']),
        'agents': run_ruv_swarm_command(['agent_list']),
        'local_monitor': run_ruv_swarm_command(['monitor', '2000']),
    }

    with data_lock:
        monitor_data.update(data)
        monitor_data['last_update'] = datetime.now().isoformat()

    return data

def collector_loop(interval=2.0):
    """Arka plan veri toplama döngüsü."""
    while True:
        try:
            collect_data()
        except Exception as e:
            with data_lock:
                monitor_data['errors'].append({
                    'time': datetime.now().isoformat(),
                    'error': str(e)
                })
        time.sleep(interval)

class MonitorHandler(BaseHTTPRequestHandler):
    """HTTP endpoint untukonitor verisi."""

    def do_GET(self):
        parsed = urlparse(self.path)

        if parsed.path == '/health':
            self.send_response(200)
            self.send_header('Content-Type', 'application/json')
            self.end_headers()
            with data_lock:
                self.wfile.write(json.dumps({
                    'status': 'ok',
                    'last_update': monitor_data.get('last_update'),
                    'agent_count': len(monitor_data.get('agents', {}).get('agents', []))
                }).encode())

        elif parsed.path == '/api/agents':
            self.send_response(200)
            self.send_header('Content-Type', 'application/json')
            self.end_headers()
            with data_lock:
                self.wfile.write(json.dumps(monitor_data.get('agents', {})).encode())

        elif parsed.path == '/api/swarm':
            self.send_response(200)
            self.send_header('Content-Type', 'application/json')
            self.end_headers()
            with data_lock:
                self.wfile.write(json.dumps(monitor_data.get('swarm_status', {})).encode())

        elif parsed.path == '/api/all':
            self.send_response(200)
            self.send_header('Content-Type', 'application/json')
            self.end_headers()
            with data_lock:
                self.wfile.write(json.dumps(monitor_data).encode())

        elif parsed.path == '/':
            # Simple HTML dashboard
            self.send_response(200)
            self.send_header('Content-Type', 'text/html; charset=utf-8')
            self.end_headers()
            self.wfile.write(self.get_dashboard_html().encode())

        else:
            self.send_response(404)
            self.end_headers()

    def get_dashboard_html(self):
        return '''<!DOCTYPE html>
<html>
<head>
    <title>Ruv-Swarm Monitor</title>
    <meta charset="utf-8">
    <meta http-equiv="refresh" content="3">
    <style>
        body { font-family: 'Consolas', 'Monospace'; background: #1a1a2e; color: #eee; margin: 0; padding: 20px; }
        h1 { color: #00d9ff; border-bottom: 1px solid #00d9ff; padding-bottom: 10px; }
        .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 20px; }
        .card { background: #16213e; border: 1px solid #0f3460; border-radius: 8px; padding: 15px; }
        .card h2 { color: #00d9ff; margin-top: 0; font-size: 1.1em; }
        table { width: 100%; border-collapse: collapse; }
        th, td { padding: 8px; text-align: left; border-bottom: 1px solid #0f3460; }
        th { color: #00d9ff; }
        .healthy { color: #00ff88; }
        .unhealthy { color: #ff4444; }
        .degraded { color: #ffaa00; }
        .unknown { color: #888; }
        .idle { color: #00d9ff; }
        .running { color: #00ff88; }
        .error { color: #ff4444; }
        .metric { display: flex; justify-content: space-between; padding: 5px 0; }
        .metric span:first-child { color: #888; }
        .status-ok { color: #00ff88; }
        .status-warn { color: #ffaa00; }
        .status-error { color: #ff4444; }
        .timestamp { color: #666; font-size: 0.9em; }
    </style>
</head>
<body>
    <h1>🔮 Ruv-Swarm / Claude-Flow Monitor</h1>
    <div class="timestamp">Son güncelleme: <span id="lastUpdate">-</span></div>

    <div class="grid">
        <div class="card">
            <h2>📊 Swarm Durumu</h2>
            <div id="swarmStatus">Yükleniyor...</div>
        </div>
        <div class="card">
            <h2>💻 Sistem Metrikleri</h2>
            <div id="metrics">Yükleniyor...</div>
        </div>
    </div>

    <div class="card" style="margin-top: 20px;">
        <h2>🤖 Agents</h2>
        <table id="agentsTable">
            <thead>
                <tr><th>ID</th><th>Tip</th><th>İsim</th><th>Durum</th><th>Sağlık</th><th>Görevler</th></tr>
            </thead>
            <tbody></tbody>
        </table>
    </div>

    <script>
        async function loadData() {
            try {
                const res = await fetch('/api/all');
                const data = await res.json();
                updateDashboard(data);
            } catch (e) {
                console.error(e);
            }
        }

        function updateDashboard(data) {
            document.getElementById('lastUpdate').textContent = data.last_update || '-';

            // Swarm status
            const swarm = data.swarm_status || {};
            document.getElementById('swarmStatus').innerHTML = `
                <div class="metric"><span>Swarm ID:</span><span>${swarm.swarmId || 'N/A'}</span></div>
                <div class="metric"><span>Topoloji:</span><span>${swarm.topology || 'N/A'}</span></div>
                <div class="metric"><span>Max Agent:</span><span>${swarm.maxAgents || 'N/A'}</span></div>
                <div class="metric"><span>Agent Sayısı:</span><span>${swarm.agentCount || 0}</span></div>
                <div class="metric"><span>Durum:</span><span class="${swarm.status === 'running' ? 'status-ok' : 'status-warn'}">${swarm.status || 'N/A'}</span></div>
            `;

            // Metrics
            const local = data.local_monitor || {};
            const snapshots = local.snapshots || [];
            const latest = snapshots[snapshots.length - 1] || {};
            const mem = latest.memory || {};
            document.getElementById('metrics').innerHTML = `
                <div class="metric"><span>RSS:</span><span>${(mem.rss / 1024 / 1024).toFixed(1)} MB</span></div>
                <div class="metric"><span>Heap:</span><span>${(mem.heapUsed / 1024 / 1024).toFixed(1)} MB</span></div>
                <div class="metric"><span>Snapshots:</span><span>${snapshots.length}</span></div>
                <div class="metric"><span>Son Ölçüm:</span><span>${latest.timestamp || '-'}</span></div>
            `;

            // Agents
            const agents = data.agents?.agents || [];
            const tbody = document.querySelector('#agentsTable tbody');
            tbody.innerHTML = agents.map(a => `
                <tr>
                    <td>${(a.agentId || '').slice(0, 30)}</td>
                    <td>${a.agentType || 'N/A'}</td>
                    <td>${a.name || 'N/A'}</td>
                    <td class="${a.status || 'unknown'}">${a.status || 'unknown'}</td>
                    <td class="${a.health || 'unknown'}">${a.health || 'unknown'}</td>
                    <td>${a.taskCount || 0}</td>
                </tr>
            `).join('') || '<tr><td colspan="6">Agent yok</td></tr>';
        }

        loadData();
        setInterval(loadData, 3000);
    </script>
</body>
</html>'''

def run_daemon(port=8765, interval=2.0):
    """Daemon olarak çalıştır."""
    print(f"🔮 Ruv-Swarm Monitor Daemon başlatılıyor (port {port})...")

    # Collector thread
    collector = threading.Thread(target=collector_loop, args=(interval,), daemon=True)
    collector.start()

    # Initial collection
    collect_data()

    # HTTP server
    server = HTTPServer(('localhost', port), MonitorHandler)
    print(f"✅ Dashboard: http://localhost:{port}")
    print(f"✅ API: http://localhost:{port}/api/all")
    print("🛑 Durdurmak için Ctrl+C")

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\n🛑 Daemon durduruluyor...")
        server.shutdown()

if __name__ == '__main__':
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument('--port', type=int, default=8765)
    parser.add_argument('--interval', type=float, default=2.0)
    parser.add_argument('--once', action='store_true')
    args = parser.parse_args()

    if args.once:
        data = collect_data()
        print(json.dumps(data, indent=2))
    else:
        run_daemon(args.port, args.interval)