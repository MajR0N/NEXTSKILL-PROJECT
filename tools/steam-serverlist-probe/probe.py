#!/usr/bin/env python3
"""
V9 probe - does IGameServersService/GetServerList work the way 057 needs?

Answers, against the real API with your own key:
  * does it return community servers for CS2 (730) and CS:GO legacy (4465480)?
  * how many, and how long does the call take?
  * does a big query TIME OUT - which is the real constraint, not the call rate
  * do partitioned queries get round it?

Run:
    export STEAM_API_KEY=...        # never pass the key as an argument:
    python3 probe.py                # argv is visible to other processes

Get a key: https://steamcommunity.com/dev/apikey

ponytail: stdlib only, no requests, no jq. Ceiling: it counts servers and
times calls; it does not geolocate them, so "how many are Polish" needs a
GeoIP pass this deliberately does not do. Upgrade path: feed the addresses
to a GeoIP database once the polling design is real.
"""

import json
import os
import ssl
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

API = "https://api.steampowered.com/IGameServersService/GetServerList/v1/"

CS2 = 730
CSGO_LEGACY = 4465480  # confirmed from Steam's own appmanifest, 2026-08-22 (V8)

# Master Server Query Protocol regions. There is NO country filter - this is
# the coarsest geography available, which is the finding that matters.
REGION_EUROPE = 3
REGION_WORLD = 255

TIMEOUT_S = 60


def redact(text, secret):
    """The key must never reach stdout, a log or a pasted result."""
    return text.replace(secret, "<KEY>") if secret and secret in text else text


def query(key, flt, limit):
    """Returns (elapsed_seconds, server_list, error_or_None)."""
    params = urllib.parse.urlencode({"key": key, "filter": flt, "limit": limit})
    started = time.monotonic()
    try:
        ctx = ssl.create_default_context()
        with urllib.request.urlopen(API + "?" + params, timeout=TIMEOUT_S, context=ctx) as r:
            body = json.loads(r.read().decode("utf-8"))
        elapsed = time.monotonic() - started
        return elapsed, body.get("response", {}).get("servers", []), None
    except urllib.error.HTTPError as e:
        return time.monotonic() - started, [], f"HTTP {e.code}"
    except Exception as e:                                    # timeout, TLS, malformed JSON
        return time.monotonic() - started, [], redact(f"{type(e).__name__}: {e}", key)


def run(label, key, flt, limit=20000):
    elapsed, servers, err = query(key, flt, limit)
    n = len(servers)
    status = err if err else ("TRUNCATED?" if n >= limit else "ok")
    print(f"  {label:<38} {n:>6}  {elapsed:6.1f}s  {status}")
    return n, elapsed, servers, err


def main():
    key = os.environ.get("STEAM_API_KEY", "").strip()
    if not key:
        print("Set STEAM_API_KEY first. Get one at https://steamcommunity.com/dev/apikey")
        print("Use the environment, not an argument - argv is visible to other processes.")
        return 1

    print("V9 probe - GetServerList\n")
    print(f"  {'QUERY':<38} {'COUNT':>6}  {'TIME':>7}  STATUS")
    print("  " + "-" * 62)

    # 1. Does it answer at all, for both games?
    run("CS2 / Europe", key, rf"\appid\{CS2}\region\{REGION_EUROPE}")
    run("CS:GO legacy / Europe", key, rf"\appid\{CSGO_LEGACY}\region\{REGION_EUROPE}")

    # 2. The query 057 actually wants, and the one most likely to time out.
    n_world, t_world, world, err = run("CS2 / world", key, rf"\appid\{CS2}\region\{REGION_WORLD}")

    # 3. Partitioning - the way round a timeout, since there is no resume.
    print()
    print("  partitioned (there is no way to resume a timed-out query):")
    run("CS2 / Europe / occupied", key, rf"\appid\{CS2}\region\{REGION_EUROPE}\empty\1")
    run("CS2 / Europe / dedicated", key, rf"\appid\{CS2}\region\{REGION_EUROPE}\dedicated\1")
    run("CS2 / Europe / de_dust2", key, rf"\appid\{CS2}\region\{REGION_EUROPE}\map\de_dust2")

    # 4. What a row actually contains - the listing can only show what is here.
    sample = world[0] if world else None
    if sample:
        print("\n  fields returned per server:")
        for k, v in sample.items():
            shown = str(v)[:48]
            print(f"    {k:<16} {shown}")
    elif not err:
        print("\n  no servers returned - check the filter syntax before concluding anything")

    print("\n  Reminders:")
    print("   * there is NO country filter. Poland means GeoIP over these addresses.")
    print("   * a timed-out query cannot be resumed - partition instead of retrying.")
    print("   * 15-min polling is 96 calls/day per query; the documented key ceiling")
    print("     is far above that, so TIMEOUTS are the constraint, not call volume.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
