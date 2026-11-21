#!/usr/bin/env python3
"""
Ruv-Swarm / Claude-Flow Real-Time Terminal Monitor
===================================================
Gerçek zamanlı agent aktivite takip konsolu.
Yeni oturum açıldığında otomatik başlar.
"""

import json
import subprocess
import sys
import time
import os
import signal
from datetime import datetime
from threading import Thread, Event
from queue import Queue, Empty

# ANSI renk kodları
class Colors:
    RESET = '\033[0m'
    BOLD = '\033[1m'
    DIM = '\033[2m'

    # Foreground
    RED = '\033[91m'
    GREEN = '\033[92m'
    YELLOW = '\033[93m'
    BLUE = '\033[94m'
    MAGENTA = '\033[95m'
    CYAN = '\033[96m'
    WHITE = '\033[97m'
    GRAY = '\033[90m'

    # Background
    BG_RED = '\033[101m'
    BG_GREEN = '\033[102m'
    BG_YELLOW = '\033[103m'
    BG_BLUE = '\033[104m'

    # Cursor
    CLEAR = '\033[2J'
    CLEAR_LINE = '\033[2K'
    HOME = '\033[H'
    SAVE_CURSOR = '\033[s'
    RESTORE_CURSOR = '\033[u'
    HIDE_CURSOR = '\033[?25l'
    SHOW_CURSOR = '\033[?25h'

def run_mcp_tool(tool_name, args=None):
    """MCP aracını çağır ve JSON sonucu döndür."""
    try:
        # MCP tool'ları doğrudan çağırılamıyor, claude-flow CLI veya subprocess kullanmalıyız
        # Alternatif: mcp__claude-flow__* tool'ları bu script'ten çağırılamaz
        # Bu yüzden ruv-swarm CLI'sını kullanacağız
        pass
    except Exception as e:
        return {"error": str(e)}
    return None

def run_ruv_swarm_command(cmd_args):
    """ruv-swarm CLI komutu çalıştır (PowerShell script)."""
    try:
        # ruv-swarm is a .ps1 script, run via PowerShell
        ps_cmd = ['powershell', '-NoProfile', '-Command', 'ruv-swarm'] + cmd_args
        result = subprocess.run(
            ps_cmd,
            capture_output=True,
            text=True,
            timeout=15
        )
        if result.returncode == 0:
            # JSON parse etme dene
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

def run_claude_flow_mcp(tool_name, params=None):
    """Claude Flow MCP aracını çağır (JSON-RPC over stdio)."""
    # Bu doğrudan çalışmaz, MCP server ile iletişim gerektirir
    # Alternatif: claude-flow CLI varsa kullan
    try:
        result = subprocess.run(
            ['claude-flow', 'mcp', 'call', tool_name, json.dumps(params or {})],
            capture_output=True,
            text=True,
            timeout=15
        )
        if result.returncode == 0:
            return json.loads(result.stdout)
    except:
        pass
    return None

class SwarmMonitor:
    def __init__(self, refresh_interval=2.0):
        self.refresh_interval = refresh_interval
        self.running = False
        self.stop_event = Event()
        self.data_queue = Queue()
        self.last_data = {}
        self.start_time = time.time()

    def fetch_data(self):
        """Tüm swarm verilerini topla."""
        data = {
            'timestamp': datetime.now().isoformat(),
            'swarm_status': None,
            'agents': [],
            'agent_health': [],
            'tasks': [],
            'system_health': None,
            'system_metrics': None,
        }

        # ruv-swarm status
        data['swarm_status'] = run_ruv_swarm_command(['status'])

        # ruv-swarm agent list
        data['agents'] = run_ruv_swarm_command(['agent_list']) or {'agents': []}

        # ruv-swarm monitor (local process metrics)
        data['local_monitor'] = run_ruv_swarm_command(['monitor', str(int(self.refresh_interval * 1000))])

        # Note: claude-flow MCP tools cannot be called from external script directly
        # They only work within Claude Code session
        # We'll display what we can from ruv-swarm CLI

        return data

    def format_uptime(self, seconds):
        """Saniyeyi okunabilir sürece çevir."""
        m, s = divmod(int(seconds), 60)
        h, m = divmod(m, 60)
        if h > 0:
            return f"{h}h {m}m {s}s"
        elif m > 0:
            return f"{m}m {s}s"
        return f"{s}s"

    def get_health_color(self, health):
        """Sağlık durumuna göre renk döndür."""
        if health == 'healthy' or health == 'unknown':
            return Colors.GREEN
        elif health == 'degraded':
            return Colors.YELLOW
        elif health == 'unhealthy':
            return Colors.RED
        return Colors.GRAY

    def get_status_color(self, status):
        """Agent durumuna göre renk."""
        colors = {
            'idle': Colors.BLUE,
            'running': Colors.GREEN,
            'busy': Colors.YELLOW,
            'stopped': Colors.RED,
            'error': Colors.RED,
        }
        return colors.get(status, Colors.WHITE)

    def render_header(self):
        """Üst başlık çubuğu."""
        uptime = self.format_uptime(time.time() - self.start_time)
        swarm = self.last_data.get('swarm_status', {})
        swarm_id = swarm.get('swarmId', 'N/A') if isinstance(swarm, dict) else 'N/A'
        topology = swarm.get('topology', 'N/A') if isinstance(swarm, dict) else 'N/A'
        agent_count = swarm.get('agentCount', 0) if isinstance(swarm, dict) else 0

        lines = []
        lines.append(f"{Colors.BOLD}{Colors.CYAN}╔═══════════════════════════════════════════════════════════════════════════════╗{Colors.RESET}")
        lines.append(f"{Colors.BOLD}{Colors.CYAN}║{Colors.RESET}  {Colors.BOLD}{Colors.WHITE}🔮 RUV-SWARM / CLAUDE-FLOW REAL-TIME MONITOR{Colors.RESET}                                    {Colors.CYAN}║{Colors.RESET}")
        lines.append(f"{Colors.CYAN}╠═══════════════════════════════════════════════════════════════════════════════╣{Colors.RESET}")
        lines.append(f"{Colors.CYAN}║{Colors.RESET}  Swarm: {Colors.BOLD}{swarm_id[:40]}{Colors.RESET}  Topology: {Colors.YELLOW}{topology}{Colors.RESET}  Agents: {Colors.GREEN}{agent_count}{Colors.RESET}  Uptime: {Colors.MAGENTA}{uptime}{Colors.RESET}  {Colors.CYAN}║{Colors.RESET}")
        lines.append(f"{Colors.BOLD}{Colors.CYAN}╚═══════════════════════════════════════════════════════════════════════════════╝{Colors.RESET}")
        return '\n'.join(lines)

    def render_agents_table(self):
        """Agent listesi tablosu."""
        agents_data = self.last_data.get('agents', {})
        agents = agents_data.get('agents', []) if isinstance(agents_data, dict) else []

        if not agents:
            return f"{Colors.YELLOW}  Henüz agent yok. 'ruv-swarm spawn' ile oluşturun.{Colors.RESET}"

        lines = []
        lines.append(f"{Colors.BOLD}  {'ID':<32} {'TYPE':<12} {'NAME':<20} {'STATUS':<10} {'HEALTH':<10} {'TASKS':>6} {'UPTIME':>10}{Colors.RESET}")
        lines.append(f"  {'─'*32} {'─'*12} {'─'*20} {'─'*10} {'─'*10} {'─'*6} {'─'*10}")

        for agent in agents:
            agent_id = agent.get('agentId', 'N/A')[:30]
            agent_type = agent.get('agentType', 'N/A')[:10]
            name = agent.get('name', 'N/A')[:18]
            status = agent.get('status', 'unknown')
            health = agent.get('health', 'unknown')
            task_count = agent.get('taskCount', 0)

            # Uptime hesapla
            created = agent.get('createdAt', '')
            uptime_str = 'N/A'
            if created:
                try:
                    created_dt = datetime.fromisoformat(created.replace('Z', '+00:00'))
                    uptime_sec = (datetime.now(created_dt.tzinfo) - created_dt).total_seconds()
                    uptime_str = self.format_uptime(uptime_sec)
                except:
                    pass

            status_color = self.get_status_color(status)
            health_color = self.get_health_color(health)

            # Health indicator
            health_icon = "✓" if health == 'healthy' else "⚠" if health == 'degraded' else "✗" if health == 'unhealthy' else "?"

            lines.append(
                f"  {Colors.GRAY}{agent_id}{Colors.RESET} "
                f"{Colors.CYAN}{agent_type:<12}{Colors.RESET} "
                f"{Colors.WHITE}{name:<20}{Colors.RESET} "
                f"{status_color}{status:<10}{Colors.RESET} "
                f"{health_color}{health_icon} {health:<8}{Colors.RESET} "
                f"{Colors.MAGENTA}{task_count:>6}{Colors.RESET} "
                f"{Colors.BLUE}{uptime_str:>10}{Colors.RESET}"
            )

        return '\n'.join(lines)

    def render_tasks_table(self):
        """Görev listesi."""
        # ruv-swarm CLI'da task list komutu yok, boş döndür
        return f"{Colors.GRAY}  Görev listesi: ruv-swarm CLI'da task list komutu mevcut değil.{Colors.RESET}"

    def render_system_health(self):
        """Sistem sağlığı."""
        local = self.last_data.get('local_monitor', {})

        if 'error' in local:
            return f"{Colors.RED}  Local monitor hatası: {local['error']}{Colors.RESET}"

        snapshots = local.get('snapshots', [])
        if not snapshots:
            return f"{Colors.YELLOW}  Local monitor verisi yok.{Colors.RESET}"

        latest = snapshots[-1]
        memory = latest.get('memory', {})
        rss_mb = memory.get('rss', 0) / 1024 / 1024
        heap_mb = memory.get('heapUsed', 0) / 1024 / 1024

        lines = []
        lines.append(f"{Colors.BOLD}  📊 LOCAL PROCESS METRICS{Colors.RESET}")
        lines.append(f"  {'─'*50}")
        lines.append(f"  RSS Memory:     {Colors.CYAN}{rss_mb:.1f} MB{Colors.RESET}")
        lines.append(f"  Heap Used:      {Colors.CYAN}{heap_mb:.1f} MB{Colors.RESET}")
        lines.append(f"  Snapshots:      {Colors.CYAN}{len(snapshots)}{Colors.RESET}")
        lines.append(f"  Last Update:    {Colors.CYAN}{latest.get('timestamp', 'N/A')}{Colors.RESET}")

        return '\n'.join(lines)

    def render_footer(self):
        """Alt bilgi çubuğu."""
        now = datetime.now().strftime('%H:%M:%S')
        return f"{Colors.DIM}  [Ctrl+C] Çıkış  |  [R] Yenile  |  Son güncelleme: {now}  |  Refresh: {self.refresh_interval}s{Colors.RESET}"

    def render(self):
        """Tam ekran render."""
        # Clear screen and home cursor
        output = []
        output.append(Colors.CLEAR + Colors.HOME)
        output.append(self.render_header())
        output.append("")
        output.append(f"{Colors.BOLD}  🤖 AGENTS{Colors.RESET}")
        output.append(self.render_agents_table())
        output.append("")
        output.append(f"{Colors.BOLD}  📋 TASKS{Colors.RESET}")
        output.append(self.render_tasks_table())
        output.append("")
        output.append(self.render_system_health())
        output.append("")
        output.append(self.render_footer())

        sys.stdout.write('\n'.join(output))
        sys.stdout.flush()

    def data_collector(self):
        """Arka planda veri toplayıcı."""
        while not self.stop_event.is_set():
            data = self.fetch_data()
            self.data_queue.put(data)
            time.sleep(self.refresh_interval)

    def run(self):
        """Ana döngü."""
        print(f"{Colors.HIDE_CURSOR}", end='')
        self.running = True

        # Veri toplama thread'i başlat
        collector_thread = Thread(target=self.data_collector, daemon=True)
        collector_thread.start()

        # İlk veriyi al
        try:
            self.last_data = self.data_queue.get(timeout=5)
        except Empty:
            self.last_data = self.fetch_data()

        try:
            while self.running and not self.stop_event.is_set():
                self.render()

                # Yeni veri var mı kontrol et
                try:
                    while True:
                        self.last_data = self.data_queue.get_nowait()
                except Empty:
                    pass

                # Kısa bir süre bekle ve klavye kontrol et
                time.sleep(0.1)

        except KeyboardInterrupt:
            pass
        finally:
            self.stop_event.set()
            print(f"{Colors.SHOW_CURSOR}{Colors.CLEAR}{Colors.HOME}", end='')
            print(f"\n{Colors.GREEN}Monitor durduruldu.{Colors.RESET}")

def main():
    import argparse
    parser = argparse.ArgumentParser(description='Ruv-Swarm Real-Time Monitor')
    parser.add_argument('-i', '--interval', type=float, default=2.0, help='Refresh interval (seconds)')
    parser.add_argument('--once', action='store_true', help='Run once and exit')
    args = parser.parse_args()

    monitor = SwarmMonitor(refresh_interval=args.interval)

    if args.once:
        monitor.last_data = monitor.fetch_data()
        monitor.render()
    else:
        monitor.run()

if __name__ == '__main__':
    main()