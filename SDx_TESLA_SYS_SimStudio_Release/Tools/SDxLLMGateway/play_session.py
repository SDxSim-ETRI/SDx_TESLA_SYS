#!/usr/bin/env python3
"""Own the local LLM processes for exactly one Unity Play session (Linux).

The stdin pipe is a lifetime lease: Unity closes it when Play stops, when scripts
reload, or when the Editor exits. An optional stop file and parent identity check
cover shutdown paths where the pipe is not closed promptly. No install, model
pull, sudo, service-manager operation, or process-name based kill is performed.
"""
from __future__ import annotations

import argparse
import fcntl
import hmac
import json
import os
from pathlib import Path
import re
import select
import shutil
import signal
import socket
import subprocess
import sys
import tempfile
import time
from urllib.error import HTTPError, URLError
from urllib.parse import urlsplit
from urllib.request import Request, build_opener, ProxyHandler, HTTPRedirectHandler

from ollama_backend import ALLOWED_MODELS

HERE = Path(__file__).resolve().parent
MAX_JSON = 2 * 1024 * 1024
DEFAULTS = {
    "model": "qwen3:4b",
    "ollama_mode": "private",
    "gateway_port": 8765,
    "private_ollama_port": 11435,
    "shared_ollama_port": 11434,
    "ollama_executable": "",
    "models_path": "",
    "reuse_external_gateway": True,
    "startup_timeout_seconds": 90,
    "shutdown_timeout_seconds": 5,
    "lock_wait_seconds": 15,
    "keep_alive_seconds": 300,
}


class SessionError(RuntimeError):
    def __init__(self, code, message):
        self.code = code
        super().__init__(message)


class StopRequested(Exception):
    pass


class NoRedirect(HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        raise SessionError("redirect_blocked", "A local service attempted to redirect a request.")


def load_settings(path):
    settings = dict(DEFAULTS)
    try:
        raw = Path(path).read_bytes()
        if len(raw) > 16384:
            raise ValueError("settings file is too large")
        value = json.loads(raw)
        if not isinstance(value, dict) or any(k not in DEFAULTS for k in value):
            raise ValueError("unknown settings key or invalid JSON object")
        settings.update(value)
    except (OSError, UnicodeError, ValueError) as exc:
        raise SessionError("invalid_settings", "Check play_settings.json: " + str(exc)) from None
    if settings["model"] not in ALLOWED_MODELS:
        raise SessionError("invalid_settings", "Use the installed local model qwen3:4b or qwen3:1.7b.")
    if settings["ollama_mode"] not in ("private", "reuse"):
        raise SessionError("invalid_settings", "ollama_mode must be private or reuse.")
    for name in ("gateway_port", "private_ollama_port", "shared_ollama_port"):
        if type(settings[name]) is not int or not 1024 <= settings[name] <= 65535:
            raise SessionError("invalid_settings", name + " must be an integer from 1024 to 65535.")
    target_port = settings["private_ollama_port"] if settings["ollama_mode"] == "private" else settings["shared_ollama_port"]
    if settings["gateway_port"] == target_port:
        raise SessionError("invalid_settings", "Gateway and Ollama ports must differ.")
    if type(settings["reuse_external_gateway"]) is not bool:
        raise SessionError("invalid_settings", "reuse_external_gateway must be true or false.")
    for name, low, high in (("startup_timeout_seconds", 5, 600),
                            ("shutdown_timeout_seconds", 1, 15),
                            ("lock_wait_seconds", 1, 60),
                            ("keep_alive_seconds", 0, 300)):
        if type(settings[name]) is not int or not low <= settings[name] <= high:
            raise SessionError("invalid_settings", "%s must be an integer from %s to %s." % (name, low, high))
    for name in ("ollama_executable", "models_path"):
        if not isinstance(settings[name], str) or "\x00" in settings[name]:
            raise SessionError("invalid_settings", name + " must be a path string.")
    return settings


def config_path():
    custom = os.environ.get("SDX_LLM_CONFIG")
    if custom:
        return Path(custom).expanduser().absolute()
    return Path(os.environ.get("XDG_CONFIG_HOME") or Path.home() / ".config") / "sdx-simstudio/intent-gateway.json"


def atomic_json(path, value):
    path = Path(path)
    if path.is_symlink():
        raise SessionError("unsafe_status_path", "Refusing a symlinked status file.")
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, tmp = tempfile.mkstemp(prefix=".sdx-status-", dir=str(path.parent))
    try:
        os.fchmod(fd, 0o600)
        with os.fdopen(fd, "w", encoding="utf-8") as stream:
            json.dump(value, stream, ensure_ascii=False, indent=2)
            stream.write("\n")
        os.replace(tmp, path)
    finally:
        if os.path.exists(tmp):
            os.unlink(tmp)


def proc_identity(pid):
    """Linux start time distinguishes the intended parent from a recycled PID."""
    try:
        text = Path("/proc/%d/stat" % pid).read_text()
        fields = text[text.rfind(")") + 2:].split()
        if fields[0] in ("Z", "X"):
            return None
        return fields[19]
    except (OSError, IndexError, ValueError):
        return None


def child_running(process):
    # Do not call Popen.poll() here. Keeping an exited direct child unreaped
    # reserves its PID/process-group ID until group cleanup finishes, so a
    # recycled PID can never make this session signal an unrelated process.
    return proc_identity(process.pid) is not None


def group_has_live_members(process):
    if process is None:
        return False
    for item in Path("/proc").iterdir():
        if not item.name.isdigit():
            continue
        try:
            text = (item / "stat").read_text()
            fields = text[text.rfind(")") + 2:].split()
            if int(fields[2]) == process.pid and fields[0] not in ("Z", "X"):
                return True
        except (OSError, ValueError, IndexError):
            pass
    return False


def port_open(port):
    try:
        with socket.create_connection(("127.0.0.1", port), timeout=0.25):
            return True
    except OSError:
        return False


def read_config(path, port):
    """Only an existing numeric-loopback endpoint with a valid token is probed."""
    try:
        raw = Path(path).read_bytes()
        if len(raw) > 16384:
            return None
        data = json.loads(raw)
        if not isinstance(data, dict) or data.get("protocol_version") != 1:
            return None
        if data.get("endpoint") != "http://127.0.0.1:%d/v1/interpret" % port:
            return None
        if not re.fullmatch(r"[A-Za-z0-9_-]{32,128}", data.get("token", "")):
            return None
        return data
    except (OSError, UnicodeError, ValueError, TypeError):
        return None


def configured_port(path):
    """Recognize any existing valid local config without exposing its token."""
    try:
        raw = Path(path).read_bytes()
        if len(raw) > 16384:
            return None
        data = json.loads(raw)
        if not isinstance(data, dict) or data.get("protocol_version") != 1:
            return None
        uri = urlsplit(data.get("endpoint", ""))
        if (uri.scheme != "http" or uri.hostname != "127.0.0.1"
                or uri.username is not None or uri.password is not None
                or uri.path != "/v1/interpret" or uri.query or uri.fragment
                or uri.port is None or not 1024 <= uri.port <= 65535
                or not re.fullmatch(r"[A-Za-z0-9_-]{32,128}", data.get("token", ""))):
            return None
        return uri.port
    except (OSError, UnicodeError, ValueError, TypeError):
        return None


def readable_model_store(settings):
    """Locate existing model data; never create, copy, prune, or download weights."""
    explicit = settings["models_path"] or os.environ.get("OLLAMA_MODELS", "")
    candidates = [Path(explicit).expanduser()] if explicit else [
        Path.home() / ".ollama/models",
        Path("/usr/share/ollama/.ollama/models"),
        Path("/var/lib/ollama/.ollama/models"),
        Path("/var/lib/ollama/models"),
    ]
    model_name, tag = settings["model"].split(":", 1)
    relative = Path("manifests/registry.ollama.ai/library") / model_name / tag
    permission_denied = False
    for folder in candidates:
        try:
            with (folder / relative).open("rb") as stream:
                content = stream.read(262145)
            if len(content) > 262144:
                continue
            data = json.loads(content)
            blobs = [data.get("config", {})] + data.get("layers", [])
            if len(blobs) < 2:
                continue
            for entry in blobs:
                digest = entry.get("digest", "")
                if not re.fullmatch(r"sha256:[a-f0-9]{64}", digest):
                    raise ValueError("invalid model blob digest")
                # Open only; do not read multi-GB weights here.
                with (folder / "blobs" / digest.replace(":", "-")).open("rb"):
                    pass
            return folder.resolve()
        except PermissionError:
            permission_denied = True
        except (OSError, UnicodeError, ValueError, TypeError, AttributeError):
            pass
    if permission_denied:
        raise SessionError("model_store_permission", "The installed model directory is not readable by this Unity user. Set models_path to a readable existing Ollama model store, or choose ollama_mode=reuse for your running system service.")
    raise SessionError("ollama_model_not_installed", "Cannot find readable installed %s model data. Install/pull it separately, or set models_path in play_settings.json to the existing Ollama models directory." % settings["model"])


def find_ollama(settings):
    explicit = settings["ollama_executable"] or os.environ.get("SDX_OLLAMA_EXECUTABLE", "")
    if explicit:
        candidate = Path(explicit).expanduser()
        if candidate.is_absolute() and candidate.is_file() and os.access(candidate, os.X_OK):
            return str(candidate)
        raise SessionError("ollama_not_installed", "ollama_executable must point to an installed executable using an absolute path.")
    found = shutil.which("ollama")
    if found:
        return found
    for candidate in ("/usr/local/bin/ollama", "/usr/bin/ollama", str(Path.home() / ".local/bin/ollama")):
        if Path(candidate).is_file() and os.access(candidate, os.X_OK):
            return candidate
    raise SessionError("ollama_not_installed", "Ollama is not installed or is not executable. Install Ollama separately or set ollama_executable in play_settings.json.")


class PlaySession:
    def __init__(self, args):
        self.args = args
        self.settings = dict(DEFAULTS)
        self.config = config_path()
        self.parent_start = proc_identity(args.parent_pid)
        self.stop_requested = False
        self.gateway = None
        self.ollama = None
        self.model_view = None
        self.lock_fd = None
        self.gateway_token = None
        self.opener = build_opener(ProxyHandler({}), NoRedirect())
        self.state = "starting"
        self.final_error = None

    def status(self, state, message, error_code=""):
        self.state = state
        value = {
            "protocol_version": 1,
            "session_id": self.args.session_id,
            "status": state,
            "message": message,
            "error_code": error_code,
            "model": self.settings["model"],
            "gateway_owned": self.gateway is not None,
            "ollama_owned": self.ollama is not None,
            "gateway_pid": self.gateway.pid if self.gateway else 0,
            "ollama_pid": self.ollama.pid if self.ollama else 0,
            "log_path": self.args.log_path,
            "ollama_mode": self.settings["ollama_mode"],
        }
        atomic_json(self.args.status_file, value)
        print("[SDx Play LLM] %s%s: %s" % (state, " (" + error_code + ")" if error_code else "", message), flush=True)

    def check_lease(self):
        if self.stop_requested:
            raise StopRequested()
        if self.args.stop_file and Path(self.args.stop_file).exists():
            raise StopRequested()
        if self.parent_start is None or proc_identity(self.args.parent_pid) != self.parent_start:
            raise StopRequested()
        try:
            ready, _, _ = select.select([sys.stdin.fileno()], [], [], 0)
            if ready:
                data = os.read(sys.stdin.fileno(), 4096)
                if not data or b"stop" in data.lower():
                    raise StopRequested()
        except (OSError, ValueError):
            raise StopRequested() from None

    def pause(self, seconds):
        deadline = time.monotonic() + seconds
        while time.monotonic() < deadline:
            self.check_lease()
            time.sleep(min(0.1, max(0, deadline - time.monotonic())))

    def request(self, port, path, token=None):
        self.check_lease()
        headers = {"Accept": "application/json"}
        if token:
            headers["X-SDx-Token"] = token
        request = Request("http://127.0.0.1:%d%s" % (port, path), headers=headers)
        try:
            with self.opener.open(request, timeout=0.8) as response:
                body = response.read(MAX_JSON + 1)
                if len(body) > MAX_JSON:
                    return None
                data = json.loads(body)
                return data if isinstance(data, dict) else None
        except (HTTPError, URLError, OSError, ValueError, UnicodeError):
            return None
        finally:
            self.check_lease()

    def acquire_lock(self):
        self.config.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
        lock_path = self.config.parent / "play-session.lock"
        try:
            self.lock_fd = os.open(lock_path, os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW, 0o600)
        except OSError as exc:
            raise SessionError("session_lock_failed", "Cannot open the local Play session lock: " + str(exc)) from None
        end = time.monotonic() + self.settings["lock_wait_seconds"]
        waiting = False
        while True:
            self.check_lease()
            try:
                fcntl.flock(self.lock_fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
                os.ftruncate(self.lock_fd, 0)
                os.write(self.lock_fd, (self.args.session_id + "\n").encode("ascii"))
                return
            except BlockingIOError:
                if not waiting:
                    self.status("starting", "Waiting for the preceding Play session to finish cleanup.")
                    waiting = True
                if time.monotonic() >= end:
                    raise SessionError("another_play_session", "Another Unity Play session owns this local gateway. Stop that Play session before starting this one.")
                self.pause(0.1)

    def healthy_gateway(self):
        port = self.settings["gateway_port"]
        cfg = read_config(self.config, port)
        if cfg is None:
            return False
        health = self.request(port, "/health", cfg["token"])
        if (not health or health.get("protocol_version") != 1 or health.get("status") != "ready"
                or health.get("provider") != "ollama-local" or health.get("model") != self.settings["model"]
                or health.get("api_key_required") is not False
                or not isinstance(health.get("environment"), dict)
                or health["environment"].get("local_model_verified") is not True):
            return False
        # An old manually-started gateway has no owner ID. Never attach to an
        # orphaned/about-to-stop Play-owned server from another session.
        owner = health.get("play_session_id", "")
        if owner and owner != self.args.session_id:
            return False
        if self.gateway is not None and owner != self.args.session_id:
            return False
        self.gateway_token = cfg["token"]
        return True

    def child_environment(self):
        env = dict(os.environ)
        for name in ("OPENAI_API_KEY", "OPENAI_MODEL", "SDX_REASONING_EFFORT", "OLLAMA_API_KEY"):
            env.pop(name, None)
        env["OLLAMA_NO_CLOUD"] = "1"
        env["OLLAMA_NOPRUNE"] = "1"
        env["PYTHONUNBUFFERED"] = "1"
        env["PYTHONDONTWRITEBYTECODE"] = "1"
        return env

    def make_model_view(self, store):
        # Ollama versions may migrate/prune directory entries even when
        # OLLAMA_NOPRUNE is set. Give this process its own tiny mutable index.
        # Only the selected manifest is copied; large blob data is read through
        # file symlinks. Original manifests/blob directory are never given to
        # Ollama as a writable model store.
        model_name, tag = self.settings["model"].split(":", 1)
        relative = Path("manifests/registry.ollama.ai/library") / model_name / tag
        content = (store / relative).read_bytes()
        if len(content) > 262144:
            raise SessionError("invalid_model_manifest", "The installed model manifest is too large.")
        manifest = json.loads(content)
        blobs = [manifest.get("config", {})] + manifest.get("layers", [])
        self.model_view = Path(tempfile.mkdtemp(prefix=".sdx-models-" + self.args.session_id[:12] + "-",
                                               dir=str(Path(self.args.status_file).parent)))
        dest_manifest = self.model_view / relative
        dest_manifest.parent.mkdir(parents=True)
        dest_manifest.write_bytes(content)
        (self.model_view / "blobs").mkdir()
        for entry in blobs:
            self.check_lease()
            digest = entry.get("digest", "")
            if not re.fullmatch(r"sha256:[a-f0-9]{64}", digest):
                raise SessionError("invalid_model_manifest", "The installed model references an invalid blob digest.")
            name = digest.replace(":", "-")
            target = (store / "blobs" / name).resolve(strict=True)
            link = self.model_view / "blobs" / name
            if not link.is_symlink():
                link.symlink_to(target)
        return self.model_view

    def spawn(self, command, env):
        self.check_lease()
        # Descendants inherit this POSIX session/group. It is the only group
        # this supervisor may signal; no PID from a config is ever terminated.
        return subprocess.Popen(command, cwd=str(HERE), env=env, stdin=subprocess.DEVNULL,
                                start_new_session=True, close_fds=True)

    def begin(self):
        self.status("starting", "Starting the local LLM for Unity Play.")
        self.settings = load_settings(self.args.settings)
        self.check_lease()
        self.acquire_lock()
        port = self.settings["gateway_port"]
        existing_port = configured_port(self.config)
        if existing_port is not None and existing_port != port and port_open(existing_port):
            raise SessionError("gateway_config_in_use", "The local token file belongs to an active service on port %d. Stop that manual gateway or use a separate SDX_LLM_CONFIG; its token file was preserved." % existing_port)
        if port_open(port):
            if self.settings["reuse_external_gateway"] and self.healthy_gateway():
                self.status("ready", "Existing verified external gateway reused. This Play session does not own or stop that gateway or its Ollama service.")
                return
            raise SessionError("gateway_port_in_use", "Port %d is in use without a matching healthy local gateway. Close the old manual gateway or correct its model/config; no existing process was stopped." % port)
        env = self.child_environment()
        private = self.settings["ollama_mode"] == "private"
        ollama_port = self.settings["private_ollama_port"] if private else self.settings["shared_ollama_port"]
        ollama_url = "http://127.0.0.1:%d" % ollama_port
        if port_open(ollama_port):
            if private:
                raise SessionError("ollama_private_port_in_use", "Private Ollama port %d is already in use. Close its owner or choose another private_ollama_port; no existing process was stopped." % ollama_port)
            metadata = self.request(ollama_port, "/api/version")
            if not metadata or not isinstance(metadata.get("version"), str):
                raise SessionError("ollama_port_in_use", "The shared Ollama port is in use but does not respond as Ollama.")
            self.status("starting", "Using the existing shared Ollama service; it will remain running after Play.")
        else:
            executable = find_ollama(self.settings)
            store = readable_model_store(self.settings)
            env["OLLAMA_HOST"] = "127.0.0.1:%d" % ollama_port
            env["OLLAMA_MODELS"] = str(self.make_model_view(store))
            env["OLLAMA_KEEP_ALIVE"] = str(self.settings["keep_alive_seconds"]) + "s"
            self.ollama = self.spawn([executable, "serve"], env)
            self.status("starting", "Starting the Play-owned Ollama service on 127.0.0.1:%d." % ollama_port)
            deadline = time.monotonic() + self.settings["startup_timeout_seconds"]
            while True:
                self.check_lease()
                if not child_running(self.ollama):
                    raise SessionError("ollama_start_failed", "Ollama exited during startup. See the Play session log for permissions, driver, or port errors.")
                metadata = self.request(ollama_port, "/api/version")
                if metadata and isinstance(metadata.get("version"), str):
                    self.pause(0.1)
                    if child_running(self.ollama):
                        break
                if time.monotonic() >= deadline:
                    raise SessionError("ollama_start_timeout", "Ollama did not become available before the startup timeout.")
                self.pause(0.2)
        tags = self.request(ollama_port, "/api/tags")
        if not tags or not isinstance(tags.get("models"), list):
            raise SessionError("ollama_invalid_model_list", "Ollama did not return its installed model list.")
        if not any(isinstance(item, dict) and self.settings["model"] in (item.get("name"), item.get("model")) for item in tags["models"]):
            raise SessionError("ollama_model_not_installed", "The selected Ollama service does not have %s installed. Install the model separately; Play will not download it." % self.settings["model"])
        env["SDX_OLLAMA_MODEL"] = self.settings["model"]
        env["SDX_OLLAMA_URL"] = ollama_url
        env["SDX_OLLAMA_KEEP_ALIVE"] = str(self.settings["keep_alive_seconds"] if self.ollama else 0)
        env["SDX_LLM_CONFIG"] = str(self.config)
        self.gateway = self.spawn([sys.executable, "-u", str(HERE / "server.py"), "--port", str(port),
                                   "--model", self.settings["model"], "--play-session-id", self.args.session_id], env)
        self.status("starting", "Starting the local intent gateway and verifying the installed model metadata.")
        deadline = time.monotonic() + self.settings["startup_timeout_seconds"]
        while True:
            self.check_lease()
            if not child_running(self.gateway):
                raise SessionError("gateway_start_failed", "The intent gateway exited during startup. See the Play log; check the installed model, model permissions, and port.")
            if self.ollama and not child_running(self.ollama):
                raise SessionError("ollama_stopped", "The Play-owned Ollama service exited during gateway startup.")
            if self.healthy_gateway():
                self.status("ready", "Local LLM ready: %s. The first language request may take longer while the model loads." % self.settings["model"])
                return
            if time.monotonic() >= deadline:
                raise SessionError("gateway_start_timeout", "The intent gateway did not become ready before the startup timeout.")
            self.pause(0.2)

    def monitor(self):
        next_health = time.monotonic() + 5
        health_failures = 0
        while True:
            self.pause(0.25)
            for process, name in ((self.gateway, "gateway"), (self.ollama, "ollama")):
                if process is not None and not child_running(process):
                    raise SessionError(name + "_stopped", "The Play-owned %s process exited. Stop Play, inspect its log, then start Play again." % name)
            if time.monotonic() >= next_health:
                health_failures = 0 if self.healthy_gateway() else health_failures + 1
                next_health = time.monotonic() + 5
                if health_failures >= 3:
                    raise SessionError("gateway_lost", "The local gateway stopped responding. No new natural-language action will be started.")

    @staticmethod
    def signal_owned_group(process, sig):
        if process is None:
            return
        try:
            os.killpg(process.pid, sig)
        except ProcessLookupError:
            pass

    def cleanup(self):
        owned = [p for p in (self.gateway, self.ollama) if p is not None]
        # Signal both groups before waiting so active inference cannot hold
        # shutdown hostage. Ollama runner descendants receive the signal too.
        for process in owned:
            self.signal_owned_group(process, signal.SIGTERM)
        end = time.monotonic() + self.settings["shutdown_timeout_seconds"]
        while time.monotonic() < end:
            if not any(group_has_live_members(process) for process in owned):
                break
            time.sleep(0.05)
        for process in owned:
            if group_has_live_members(process):
                self.signal_owned_group(process, signal.SIGKILL)
        for process in owned:
            try:
                process.wait(timeout=1)
            except subprocess.TimeoutExpired:
                pass
        if self.gateway is not None:
            try:
                cfg = read_config(self.config, self.settings["gateway_port"])
                if (cfg and cfg.get("play_session_id") == self.args.session_id
                        and (self.gateway_token is None or hmac.compare_digest(cfg["token"], self.gateway_token))):
                    self.config.unlink()
            except OSError:
                pass
        if self.model_view is not None:
            # shutil.rmtree removes file symlinks, never their source blobs.
            try:
                shutil.rmtree(self.model_view)
            except OSError as exc:
                print("[SDx Play LLM] Temporary model view cleanup: " + str(exc), file=sys.stderr, flush=True)
            self.model_view = None
        if self.lock_fd is not None:
            os.close(self.lock_fd)
            self.lock_fd = None

    def run(self):
        for sig in (signal.SIGINT, signal.SIGTERM, signal.SIGHUP):
            signal.signal(sig, lambda *_: setattr(self, "stop_requested", True))
        code = 0
        try:
            self.begin()
            self.monitor()
        except StopRequested:
            pass
        except SessionError as exc:
            self.final_error = exc
            code = 1
        except (OSError, ValueError, TypeError) as exc:
            self.final_error = SessionError("play_start_failed", "Local LLM startup failed: " + str(exc))
            code = 1
        finally:
            self.cleanup()
            try:
                if self.final_error:
                    self.status("error", str(self.final_error), self.final_error.code)
                else:
                    self.status("stopped", "Play session ended; its owned LLM processes were stopped.")
            except (OSError, SessionError) as exc:
                print("[SDx Play LLM] Could not write final status: " + str(exc), file=sys.stderr, flush=True)
        return code


def redirect_log(path):
    """Keep logs alive across Unity domain reload; children share these FDs."""
    if not path:
        return
    target = Path(path)
    if not target.is_absolute() or target.is_symlink():
        raise ValueError("log-path must be an absolute non-symlink path")
    target.parent.mkdir(parents=True, exist_ok=True)
    fd = os.open(target, os.O_WRONLY | os.O_CREAT | os.O_APPEND | os.O_NOFOLLOW, 0o600)
    try:
        sys.stdout.flush()
        sys.stderr.flush()
        os.dup2(fd, 1)
        os.dup2(fd, 2)
    finally:
        if fd > 2:
            os.close(fd)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--status-file", required=True)
    parser.add_argument("--session-id", required=True)
    parser.add_argument("--parent-pid", type=int, required=True)
    parser.add_argument("--stop-file", default="")
    parser.add_argument("--log-path", default="")
    parser.add_argument("--settings", default=str(HERE / "play_settings.json"))
    args = parser.parse_args()
    if not sys.platform.startswith("linux"):
        parser.error("This launcher targets the Ubuntu/Linux project.")
    if not re.fullmatch(r"[a-f0-9]{32}", args.session_id):
        parser.error("session-id must be 32 lowercase hexadecimal characters")
    if args.parent_pid <= 1:
        parser.error("parent-pid must identify the Unity Editor/player")
    if not Path(args.status_file).is_absolute() or (args.stop_file and not Path(args.stop_file).is_absolute()):
        parser.error("status-file and stop-file must be absolute paths")
    redirect_log(args.log_path)
    return PlaySession(args).run()


if __name__ == "__main__":
    sys.exit(main())
