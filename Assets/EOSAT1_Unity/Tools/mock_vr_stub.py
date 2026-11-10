#!/usr/bin/env python3
"""
mock_vr_stub.py - an unofficial stand-in for the course's vr_stub.py.

Serves a JSON-Lines recording (default: eosat1_one_orbit.jsonl from
make_one_orbit.py) on TCP port 8100, one JSON object per line: a "hello",
then a "state" frame every tick, and prints the "status" lines the headset
sends back. Use the real vr_stub.py whenever you have it; this one exists so
the Unity side can be tested without it.

    python3 mock_vr_stub.py                       # 1x, one frame a second
    python3 mock_vr_stub.py --speed 10            # sim time 10x: frames jump 10 s
    python3 mock_vr_stub.py --chaos               # broken lines, stalls, duplicates...
    python3 mock_vr_stub.py --file my_recording.jsonl --loop

In Unity: EOSAT-1 App > Feed = Tcp, Host = this laptop's IP (127.0.0.1 when
Unity runs on the same machine), Port = 8100. Same Wi-Fi for a headset.
"""
import argparse
import json
import random
import socket
import sys
import threading
import time
from datetime import datetime, timezone

STATUS_BITS = {0: "RX_OK", 1: "PARSE_OK", 2: "RENDERING", 3: "DATA_FRESH"}


def parse_time(s):
    return datetime.strptime(s.replace("Z", ""), "%Y-%m-%dT%H:%M:%S").replace(tzinfo=timezone.utc).timestamp()


def load(path):
    hello, frames = None, []
    with open(path, encoding="utf-8") as fh:
        for raw in fh:
            raw = raw.strip()
            if not raw:
                continue
            try:
                obj = json.loads(raw)
            except ValueError:
                continue
            if obj.get("type") == "hello" and hello is None:
                hello = obj
            elif obj.get("type") == "state" and "sim_time" in obj:
                frames.append((parse_time(obj["sim_time"][:19]), obj))
    if not frames:
        sys.exit("no state frames in " + path)
    return hello or {"type": "hello", "spacecraft": "EOSAT-1"}, frames


def log(msg):
    print(time.strftime("%H:%M:%S ") + msg, flush=True)


class Session(threading.Thread):
    def __init__(self, conn, addr, hello, frames, args):
        super().__init__(daemon=True)
        self.conn, self.addr, self.hello, self.frames, self.args = conn, addr, hello, frames, args
        self.alive = True
        self.rng = random.Random(args.seed)

    def send(self, data):
        self.conn.sendall(data.encode("utf-8") if isinstance(data, str) else data)

    def reader(self):
        buf = b""
        while self.alive:
            try:
                chunk = self.conn.recv(4096)
            except OSError:
                break
            if not chunk:
                break
            buf += chunk
            while b"\n" in buf:
                line, buf = buf.split(b"\n", 1)
                text = line.decode("utf-8", "replace").strip()
                if not text:
                    continue
                try:
                    st = json.loads(text)
                    word = int(st.get("status_word", st.get("status", -1)))
                    names = [n for b, n in STATUS_BITS.items() if word >= 0 and word >> b & 1]
                    own = (word >> 8) & 0xFF if word >= 0 else 0
                    health = "healthy" if word >= 0 and word & 15 == 15 else "NOT healthy"
                    log("%s status %s (0x%04X) %s%s, last seq %s" % (
                        self.addr[0], word, max(word, 0), "|".join(names) or "-",
                        (" +own 0x%02X" % own) if own else "", st.get("last_seq", "?")) + "  -> " + health)
                except (ValueError, TypeError):
                    log("%s sent a line that is not a status: %r" % (self.addr[0], text[:80]))
        self.alive = False

    def chaos(self, line):
        """Return a list of (delay_seconds, bytes) writes for one frame line."""
        r, rng = self.rng.random(), self.rng
        if r < 0.04:
            cut = rng.randint(5, max(6, len(line) - 5))
            log("chaos: broken line (cut at %d)" % cut)
            return [(0, line[:cut] + "\n"), (0, line + "\n")]
        if r < 0.07:
            log("chaos: garbage line")
            return [(0, rng.choice(["#$%&*!", "\x00\x01\x02garbage", "{\"type\":\"state\",", "]]]", "NaN"]) + "\n"),
                    (0, line + "\n")]
        if r < 0.11:
            log("chaos: duplicate frame")
            return [(0, line + "\n"), (0, line + "\n")]
        if r < 0.14:
            log("chaos: line split across two writes")
            mid = len(line) // 2
            return [(0, line[:mid]), (0.35, line[mid:] + "\n")]
        if r < 0.17:
            log("chaos: unknown fields and an unknown message type")
            obj = json.loads(line)
            obj["x_future_field"] = {"nested": [1, 2, {"deep": True}]}
            obj.setdefault("orbit", {})["vel_unknown_units"] = "abc"
            return [(0, json.dumps({"type": "debug", "text": "ignore me"}) + "\n"),
                    (0, json.dumps(obj, separators=(",", ":")) + "\n")]
        if r < 0.19:
            log("chaos: CRLF line ending")
            return [(0, line + "\r\n")]
        if r < 0.21:
            log("chaos: blank lines")
            return [(0, "\n\n" + line + "\n\n")]
        return [(0, line + "\n")]

    def run(self):
        log("client %s:%d connected" % self.addr)
        threading.Thread(target=self.reader, daemon=True).start()
        a = self.args
        frames = self.frames
        try:
            self.send(json.dumps(self.hello, separators=(",", ":")) + "\n")
            t0_sim = frames[0][0]
            cursor = t0_sim            # sim time we are at
            idx = 0
            last_sent = -1
            tick = 1.0 / a.rate
            next_wall = time.monotonic()
            while self.alive:
                # pick the newest frame at or before the cursor
                while idx + 1 < len(frames) and frames[idx + 1][0] <= cursor:
                    idx += 1
                if idx != last_sent:
                    line = json.dumps(frames[idx][1], separators=(",", ":"))
                    writes = self.chaos(line) if a.chaos else [(0, line + "\n")]
                    for delay, data in writes:
                        if delay:
                            time.sleep(delay)
                        self.send(data)
                    last_sent = idx
                # chaos: stalls and save-point reloads
                if a.chaos:
                    r = self.rng.random()
                    if r < 0.02:
                        stall = self.rng.uniform(2.5, 6.0)
                        log("chaos: stall %.1f s (sim time keeps running)" % stall)
                        time.sleep(stall)
                        cursor += stall * a.speed
                        next_wall = time.monotonic()
                    elif r < 0.03 and cursor - t0_sim > 700:
                        back = self.rng.choice([120, 300, 600])
                        log("chaos: reload a saved point, sim time goes back %d s" % back)
                        cursor -= back
                        idx = max(0, min(range(len(frames)), key=lambda i: abs(frames[i][0] - cursor)) - 1)
                        last_sent = -1
                cursor += tick * a.speed
                if cursor > frames[-1][0]:
                    if not a.loop:
                        log("end of recording; closing %s" % self.addr[0])
                        break
                    log("end of recording; looping (sim time jumps back)")
                    cursor, idx, last_sent = t0_sim, 0, -1
                next_wall += tick
                time.sleep(max(0.0, next_wall - time.monotonic()))
        except (BrokenPipeError, ConnectionResetError, OSError) as e:
            log("client %s gone (%s)" % (self.addr[0], e.__class__.__name__))
        finally:
            self.alive = False
            try:
                self.conn.close()
            except OSError:
                pass


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--file", default="eosat1_one_orbit.jsonl")
    ap.add_argument("--host", default="0.0.0.0")
    ap.add_argument("--port", type=int, default=8100)
    ap.add_argument("--speed", type=float, default=1.0, help="sim seconds per wall second (default 1)")
    ap.add_argument("--rate", type=float, default=1.0, help="frames per wall second (default 1)")
    ap.add_argument("--chaos", action="store_true", help="broken lines, garbage, duplicates, stalls, reloads")
    ap.add_argument("--loop", action="store_true", help="start again at the end (a backwards time jump)")
    ap.add_argument("--seed", type=int, default=None)
    args = ap.parse_args()
    hello, frames = load(args.file)
    log("loaded %d frames (%s to %s)" % (len(frames), frames[0][1]["sim_time"], frames[-1][1]["sim_time"]))
    srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    srv.bind((args.host, args.port))
    srv.listen(4)
    log("listening on %s:%d  speed %gx  %g frame/s%s" % (args.host, args.port, args.speed, args.rate,
                                                       "  CHAOS" if args.chaos else ""))
    try:
        while True:
            conn, addr = srv.accept()
            conn.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
            Session(conn, addr, hello, frames, args).start()
    except KeyboardInterrupt:
        log("bye")


if __name__ == "__main__":
    main()
