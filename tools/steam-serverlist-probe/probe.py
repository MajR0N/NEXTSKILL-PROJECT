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

    python3 probe.py --selftest     # no key, no network: pins the three
                                    # judgements this publishes about real
                                    # servers against what calibrated them

Get a key: https://steamcommunity.com/dev/apikey

ponytail: stdlib only, no requests, no jq. Ceiling: it counts servers and
times calls; it does not geolocate them, so "how many are Polish" needs a
GeoIP pass this deliberately does not do. Upgrade path: feed the addresses
to a GeoIP database once the polling design is real.
"""

import collections
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

# Steam's own internal deadline, measured at 16.9-17.6 s across runs 2, 3 and 7.
# An empty response either side of this line means opposite things (run 10).
DEADLINE_S = 10

# MEASURED 2026-08-22, and it is the finding that matters: the API caps a
# response at 10 000 rows and tells you nothing. Asking for more does not help.
# A capped answer is byte-identical in shape to a complete one - which is worse
# than the timeout we expected, because a timeout is loud and this is silent.
RESPONSE_CAP = 10000

# How far past its own slot count a server must claim before we call it a fake.
# 1.5x separates CS:GO's 254/64 listings from CS2's single 11/10, which is a
# real server that dropped a slot. See `population()`.
IMPOSSIBLE_MARGIN = 1.5


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
        resp = body.get("response", {})
        if "servers" not in resp:
            # Steam answers HTTP 200 with {"response":{}} - no `servers` key -
            # for BOTH "your query died on my deadline" and "nothing matched".
            # Run 10 separated them by TIME: the sweep's empty partitions came
            # back in 2-5 s (a real zero) while the deadline failures take ~17 s.
            # Runs 1-2 called every empty a zero; runs 7-9 called every empty a
            # failure. Both were half right, and the clock is what tells them
            # apart - so this returns the fact, and run() reads the verdict off
            # the elapsed time rather than asserting one here.
            return elapsed, [], "EMPTY RESPONSE (no `servers` key)"
        return elapsed, resp["servers"], None
    except urllib.error.HTTPError as e:
        return time.monotonic() - started, [], f"HTTP {e.code}"
    except Exception as e:                                    # timeout, TLS, malformed JSON
        return time.monotonic() - started, [], redact(f"{type(e).__name__}: {e}", key)


def truncated(n, ceiling):
    """Proximity, not equality. Measured 2026-08-22: limit=5000 returned 4988 and
    limit=9999 returned 9977, so the API drops ~0.2% of rows AFTER applying the
    limit. `world / de_dust2` returned 9999 and passed an equality test while
    being as capped as the ones that returned 10000.
    """
    return n >= ceiling * 0.99


def fetch(key, flt, limit):
    """query() plus the one retry the design mandates. Returns
    (elapsed, servers, err, retried).

    Finding 4, promoted from "transient" to "recurring" by run 7: Steam answers
    200-with-no-`servers`-key after ~17 s on the largest partition asked for. The
    rule 057 s5a needs is retry, never store a zero - so the probe obeys its own
    rule and one hiccup does not cost a run. Run 9 showed why this belongs HERE
    rather than in run(): sweep() called query() directly and 18 of its 36
    partitions came back empty with no retry at all.
    """
    elapsed, servers, err = query(key, flt, limit)
    if err and err.startswith("EMPTY"):
        again, servers, err = query(key, flt, limit)
        return elapsed + again, servers, err, True
    return elapsed, servers, err, False


def run(label, key, flt, limit=20000):
    elapsed, servers, err, retried = fetch(key, flt, limit)
    note = " (after 1 retry)" if retried else ""
    n = len(servers)
    # The first run of this probe reported "ok" for three capped queries,
    # because it compared against the limit we ASKED for (20 000) rather than
    # the one the API enforces. Compare against both.
    ceiling = min(limit, RESPONSE_CAP)
    if err and err.startswith("EMPTY"):
        # ~17 s is Steam's own deadline; anything quick is the API saying zero.
        status = err + (" - SLOW, so the deadline: retry, never store"
                        if elapsed >= DEADLINE_S else
                        " - fast, so probably a real zero")
    elif err:
        status = err
    elif truncated(n, ceiling):
        status = f"TRUNCATED (ceiling {ceiling}) - partition further"
    else:
        status = "ok"
    print(f"  {label:<38} {n:>6}  {elapsed:6.1f}s  {status}{note}")
    return n, elapsed, servers, err


def is_cgnat(addr):
    """100.64.0.0/10 - RFC 6598 shared address space, used for carrier-grade
    NAT. It is NOT publicly routable and NOT geolocatable: no GeoIP database can
    place it, because the same address means different machines on different
    networks. Measured 2026-08-22: most CS2 rows are in it.
    """
    try:
        a, b = (int(x) for x in addr.split(":")[0].split(".")[:2])
    except (ValueError, TypeError):
        return False
    return a == 100 and 64 <= b <= 127


def padded(s):
    """A listing claiming more players than 1.5x its own slots. CALIBRATED, and
    the calibration is the point: a strict `>` flagged exactly one CS2 server, at
    11/10 - a real Portuguese competitive server that had almost certainly dropped
    a slot while 11 were connected. CS:GO's fakes sit at 254/64 and 254/32, and
    254 is the A2S player-count byte pinned at its ceiling. One-over is noise;
    four-times-over is fraud. V11 exists to stop us accusing the first kind.
    """
    return s.get("players", 0) > s.get("max_players", 0) * IMPOSSIBLE_MARGIN


SNAPSHOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "snapshot.json")


def compare(prev, now):
    """V17, the half no single run can answer: do these identities SURVIVE?
    Returns (survived, moved) - how many of the previous run's steamids are
    still listed, and how many of those now report a different address.

    Both numbers matter and they say opposite things. A low survival rate means
    steamid churns and 057 s5a has no key. A high survival rate with high
    address movement means steamid is the key and `addr` is NOT - which is
    exactly what a relayed CS2 listing would look like.
    """
    survived = [k for k in prev if k in now]
    moved = [k for k in survived if prev[k] != now[k]]
    return len(survived), len(moved)


def compare_addr(prev, now):
    """The REVERSE join, and run 10 is why it exists. At the first gap longer
    than a few minutes (1.7 h), CS2 steamid survival collapsed from 98% to 8.5%
    while the list itself GREW 6 157 -> 7 691. Two readings fit and they lead
    opposite ways:

      * the servers left the list (then their addresses are gone too), or
      * the same servers are still listed under NEW steamids - which would mean
        the CS2 identity is per-session, `addr` is the stable key, and the
        recommendation to key on steamid is exactly backwards.

    Joining on the address separates them. prev/now are {steamid: addr}, so
    invert. Returns (addresses still listed, of which now carry a NEW steamid).
    """
    p = {v: k for k, v in prev.items() if v}
    n = {v: k for k, v in now.items() if v}
    survived = [a for a in p if a in n]
    rekeyed = [a for a in survived if p[a] != n[a]]
    return len(survived), len(rekeyed)


def snapshot(label, servers):
    """Persist {steamid: addr} per partition, and report against the last run.
    ponytail: one JSON file next to the probe, no database. Ceiling: it keeps
    only the previous run, so this measures consecutive-run stability, not a
    month of it. Upgrade path: the poller 057 s5a needs keeps the real series.
    """
    now = {str(s["steamid"]): s.get("addr") for s in servers if s.get("steamid")}
    book = {}
    if os.path.exists(SNAPSHOT):
        try:
            book = json.load(open(SNAPSHOT, encoding="utf-8"))
        except (ValueError, OSError):
            book = {}                        # a corrupt file is not a finding
    prev = book.get(label, {})
    if prev:
        survived, moved = compare(prev, now)
        age = time.time() - book.get("_written", 0)
        addr_survived, rekeyed = compare_addr(prev, now)
        print(f"      V17 vs last run ({age / 3600:.1f}h ago): "
              f"{survived}/{len(prev)} steamids survived, {moved} changed address")
        print(f"      V17 reverse join: {addr_survived}/{len(prev)} ADDRESSES still listed,"
              f" {rekeyed} of them under a NEW steamid"
              f"  ->  {'steamid is EPHEMERAL, addr is the key' if rekeyed > survived else 'identity holds'}")
    else:
        print(f"      V17: {len(now)} identities recorded. Run again later to measure churn.")
    book[label] = now
    book["_written"] = time.time()
    with open(SNAPSHOT, "w", encoding="utf-8") as f:
        json.dump(book, f)


def population(label, key, flt):
    """V10 asks about POPULATION, not server count - and the API hands us
    `players` and `bots` per row. Sum them over a partition small enough to
    come back complete, and the question is measured rather than estimated.

    ponytail: sums the rows we already fetched, no second call. Ceiling: it
    trusts the counts, which is exactly what V11 says not to do - but V11 is
    about detecting padding over weeks, and this is one snapshot for sizing.
    """
    n, _, servers, err = run(label, key, flt)
    if err or not servers:
        return
    players = sum(s.get("players", 0) for s in servers)
    bots = sum(s.get("bots", 0) for s in servers)
    print(f"      players={players}  bots={bots}  across {n} servers")
    print(f"      if `players` includes `bots` (UNVERIFIED), humans = {players - bots}")

    # V11 asked whether padded counts are distinguishable over a multi-week
    # series. Some of them need no series at all: a server claiming FAR more
    # players than it has slots is an arithmetic impossibility, not a
    # statistical inference. "No false-accusation risk" was the over-claim -
    # see padded(), which a real server tripped on the first run. Hence a margin.
    over = [s for s in servers if padded(s)]
    full = [s for s in servers if s.get("players", 0) >= s.get("max_players", 0) > 0]
    print(f"      mean {players / n:.1f} players/server"
          f"   |  {len(over)} beyond {IMPOSSIBLE_MARGIN}x their slots"
          f"   |  {len(full)} exactly full")
    if over:
        for s in sorted(over, key=lambda s: -s.get("players", 0))[:3]:
            print(f"        {s.get('players')}/{s.get('max_players')}  {str(s.get('name'))[:40]}")

    # V10 wants an HONEST population, and two one-snapshot checks remove ~90% of
    # the CS:GO claim: a row past 1.5x its slots, and a row claiming >=255 SLOTS
    # (the byte ceiling again - impossible for a game that seats 64). What is
    # left is still an OVER-estimate: a listing claiming 64/64 is internally
    # consistent and survives both checks. Treat it as a ceiling, not a count.
    clean = [s for s in servers if not padded(s) and s.get("max_players", 0) < 255]
    clean_players = sum(s.get("players", 0) for s in clean)
    print(f"      padding-filtered: {clean_players} players on {len(clean)} servers"
          f"  (mean {clean_players / max(len(clean), 1):.1f})"
          f"  - dropped {n - len(clean)} rows, {players - clean_players} claimed players")

    # 057 s5a labels a server by watching it over WEEKS, which needs a key that
    # survives between polls. For CS2 `addr` is a relay address (see is_cgnat),
    # so it may not be one. The API hands us `steamid` - the server's GSLT
    # identity, which is issued to the operator and does not move - and the rows
    # are already here, so this costs no call. If steamid is present and distinct
    # per row, the time series is keyable even where the address is not.
    ids = [str(s.get("steamid", "")) for s in servers]
    real = [i for i in ids if i and i != "0"]
    addrs = {s.get("addr") for s in servers}
    key_verdict = ("steamid is the key" if len(set(real)) >= n
                   else "NEITHER is unique - 057 s5a has no key, and that blocks it")
    print(f"      steamid on {len(real)}/{n} ({len(set(real))} distinct)"
          f"  |  addr {len(addrs)} distinct  ->  {key_verdict}")

    # A spam farm runs thousands of listings from a handful of hosts, so listing
    # density per /24 lights it up on ONE snapshot - no time series needed. This
    # is the check that scales: the impossible-count above catches only the
    # laziest fakes, because a listing claiming 64/64 is a lie but not an
    # arithmetic error.
    nets = collections.Counter(
        ".".join(s.get("addr", "").split(":")[0].split(".")[:3]) for s in servers)
    dense = ", ".join(f"{net}.x={c}" for net, c in nets.most_common(4))
    print(f"      {len(nets)} distinct /24s for {n} servers"
          f"  ({n / max(len(nets), 1):.1f} per /24)  densest: {dense}")

    # THE finding, 2026-08-22. Address concentration only means "spam farm" if
    # the addresses are real. CS2's are not: 100.64/10 is non-routable shared
    # space, so its 70-per-/24 density is an artefact of relaying, not evidence
    # of anything - and GeoIP over these addresses cannot work at all.
    cg = sum(1 for s in servers if is_cgnat(s.get("addr", "")))
    verdict = "GeoIP IMPOSSIBLE on these" if cg > n * 0.5 else "geolocatable"
    print(f"      {cg}/{n} ({100 * cg / n:.0f}%) in 100.64/10 non-routable space - {verdict}")
    # V16 asks how a CS2 server gets onto a POLISH catalogue. The answer is not
    # all-or-nothing: whatever is left outside the relay space is geolocatable
    # today, with no plugin. Measured 2026-08-22: 267 of 6189, i.e. 4%.
    if cg:
        # Only meaningful where relay space exists. Run 7 printed "100% CAN be
        # geolocated" for CS:GO, which is true and reads as good news about a
        # list that is 30% fabricated. V16 is a CS2 question.
        print(f"      -> {n - cg} rows ({100 * (n - cg) / n:.0f}%) CAN be geolocated (V16)")

    # `max_players` is padded too, and saturates at the same byte ceiling as
    # `players`. This catches far more fakes than "players > slots" does,
    # because a server claiming 255 SLOTS is impossible without claiming
    # impossible players.
    slots = collections.Counter(s.get("max_players", 0) for s in servers)
    absurd = sum(c for k, c in slots.items() if k >= 255)
    print("      commonest max_players: "
          + ", ".join(f"{k}={c}" for k, c in slots.most_common(5))
          + f"   |  {absurd} claim >=255 SLOTS (impossible)")

    snapshot(label, servers)


def semantics(key, flt, known_name):
    """Run 9's sweep returned 1185 distinct servers where the unpartitioned query
    is capped at 10 000, and 18 of 36 letters came back empty. Two explanations
    fit - the queries failed, or `name_match` does not mean what we assumed - and
    they lead opposite ways. Test it against a name the API itself just gave us,
    before trusting any partition built on it.
    """
    print(f"  does `name_match` do what we assumed? probe name: {known_name[:32]!r}")
    word = known_name.split(" ")[0] or known_name
    # Run 10 got EMPTY for all three patterns, FAST - so either nothing matched
    # or the filter does not work at all, and those need different answers. `*`
    # alone is the control: if a bare wildcard returns nothing, `name_match` is
    # unsupported here and the whole partition strategy is dead. Case and the
    # space are the other two suspects - the probe name was 'ZACZAROWANY BURDEL'
    # (uppercase, spaced) while the sweep queries lowercase `a*`.
    for label, pattern in (("bare wildcard *", "*"),
                           ("exact", known_name),
                           ("first word", word),
                           ("prefix  Word*", word[:4] + "*"),
                           ("prefix  word* lower", word[:4].lower() + "*"),
                           ("substring *word*", "*" + word[1:5] + "*")):
        _, servers, err, _ = fetch(key, flt + rf"\name_match\{pattern}", 20000)
        hit = any(str(s.get("name", "")) == known_name for s in servers)
        print(f"    {label:<18} {len(servers):>6} rows"
              f"  {'CONTAINS the probe server' if hit else 'does NOT contain it'}"
              f"  {err or ''}")


def sweep(label, key, flt, known_name=None,
          alphabet="abcdefghijklmnopqrstuvwxyz0123456789"):
    """V10 cannot be counted while every CS:GO partition hits the cap - Europe,
    Europe+occupied+secure and even a single map all returned 10 000. So
    partition on something the cap does not correlate with: `name_match` takes a
    `*` wildcard, and a hostname's first character is unrelated to how many
    servers exist.

    Dedupes by steamid: the wildcard's semantics are undocumented, so a row
    matching two partitions must not be counted twice. What this yields is a
    LOWER BOUND - names starting with punctuation, Cyrillic or a space are not
    in the alphabet.

    ponytail: 36 calls, ~2 minutes, no pagination logic. Ceiling: still capped
    per partition - a letter returning 10 000 needs a second character.
    """
    if known_name:
        semantics(key, flt, known_name)
    seen, capped, failed = {}, [], []
    for ch in alphabet:
        flt_ch = flt + rf"\name_match\{ch}*"
        elapsed, servers, err, _ = fetch(key, flt_ch, 20000)
        if err:
            # Run 9 printed only the letter, which was not enough to tell a
            # throttled query from a genuinely empty one. The TIME says which:
            # ~17 s is Steam's deadline, a fast empty is a real zero. Both have
            # already survived one retry by the time they reach here.
            failed.append(f"{ch}({elapsed:.0f}s)")
            continue
        if truncated(len(servers), RESPONSE_CAP):
            capped.append(ch)
        for s in servers:
            sid = str(s.get("steamid", ""))
            if sid and sid not in seen:
                seen[sid] = s
    honest = [s for s in seen.values()
              if not padded(s) and s.get("max_players", 0) < 255]
    players = sum(s.get("players", 0) for s in seen.values())
    clean = sum(s.get("players", 0) for s in honest)
    print(f"  {label}")
    print(f"      {len(seen)} distinct servers across {len(alphabet)} name partitions"
          f"  ({len(capped)} still capped: {''.join(capped) or 'none'})")
    if failed:
        print(f"      {len(failed)} partitions FAILED after a retry: {' '.join(failed)}")
    print(f"      claimed {players} players  |  padding-filtered {clean} on {len(honest)}"
          f" servers  ({100 * (len(seen) - len(honest)) / max(len(seen), 1):.0f}% of swept rows"
          f" padded)  -> LOWER BOUND, letters only")



def selftest():
    """The three judgements this probe publishes about real servers, pinned by
    the measurements that calibrated them. Run: python3 probe.py --selftest
    (no key, no network). ponytail: asserts, not a framework.
    """
    # The measured false positive. `[PT] Tuga Army - Competitivo #4` at 11/10 is
    # a real server that dropped a slot; flagging it is V11's stated risk.
    assert not padded({"players": 11, "max_players": 10}), "11/10 is noise, not fraud"
    # The measured fakes: the A2S player byte at its ceiling.
    assert padded({"players": 254, "max_players": 64})
    assert padded({"players": 254, "max_players": 32})
    # A full server is not a padded one, at any size.
    assert not padded({"players": 64, "max_players": 64})
    # ...and an unfilled row must not divide-by-nothing into an accusation.
    assert not padded({}), "a row with no counts accuses nobody"

    # Truncation is proximity, not equality: both of these came back capped.
    assert truncated(10000, 10000)
    assert truncated(9999, 10000), "world/de_dust2 passed an equality test while capped"
    assert truncated(4988, 5000), "limit=5000 returned 4988 - still the ceiling"
    assert not truncated(5734, 10000), "Europe/occupied is complete; do not re-partition it"

    # GeoIP is impossible on 100.64/10 and fine on ordinary space. Getting this
    # backwards would publish a Polish catalogue built on relay addresses.
    assert is_cgnat("100.64.194.11:27015")
    assert is_cgnat("100.127.0.1:27015")
    assert not is_cgnat("100.63.255.1:27015"), "just below the block"
    assert not is_cgnat("100.128.0.1:27015"), "just above the block"
    assert not is_cgnat("91.211.116.4:27015"), "a real CS:GO farm address"
    assert not is_cgnat(""), "a missing address is not a relay address"
    # V17's join. Survival and movement say opposite things, and getting them
    # the wrong way round would report a relayed-but-stable server as churn.
    prev = {"A": "1.1.1.1", "B": "2.2.2.2", "C": "3.3.3.3"}
    now = {"A": "1.1.1.1", "B": "9.9.9.9"}          # C delisted, B moved
    assert compare(prev, now) == (2, 1)
    assert compare({}, now) == (0, 0), "a first run is not 100% churn"
    assert compare(prev, {}) == (0, 0), "a failed poll is not a mass delisting"

    # The reverse join, run 10's question: same box, new identity?
    rekey = {"A": "1.1.1.1", "B": "2.2.2.2"}
    assert compare_addr(rekey, {"Z": "1.1.1.1", "B": "2.2.2.2"}) == (2, 1),         "A's address is still listed under a new steamid - that is a re-key, not a delisting"
    assert compare_addr(rekey, {"A": "1.1.1.1"}) == (1, 0)
    assert compare_addr(rekey, {}) == (0, 0)

    print("selftest ok")
    return 0


def main():
    if "--selftest" in sys.argv:
        return selftest()

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

    # 5. Where exactly does it stop, and is `limit` honoured below the cap?
    #    Run 1 proved a cap exists by contradiction: Europe returned 9997 while
    #    Europe-AND-dedicated returned 10000, and a subset cannot exceed its
    #    superset. This measures the boundary instead of inferring it.
    # Answered: the cap is a hard 10 000, and `limit` is honoured below it but
    # loses ~0.2% of rows. What is NOT answered is the anomaly - limit=10000 and
    # 10001 both returned zero rows in ~17s, while limit=20000 returned 10 000
    # in 3.5s. Non-monotonic, so probably transient; if it reproduces it is a
    # boundary to design around rather than noise.
    print("\n  the limit=10000 anomaly - does it reproduce?")
    for attempt in (1, 2):
        run(f"CS2 / Europe / limit=10000 #{attempt}", key,
            rf"\appid\{CS2}\region\{REGION_EUROPE}", limit=10000)

    # 6. Is the appid filter honoured, or silently ignored? If a filter for
    #    4465480 quietly returns CS2 servers, the CS:GO population number that
    #    V10 rests on is measuring the wrong game. Use a filter small enough to
    #    finish under the cap, so the answer is complete.
    print("\n  is the appid filter real? (V10 rests on this)")
    _, _, csgo, _ = run("CS:GO legacy / Europe / de_dust2", key,
                        rf"\appid\{CSGO_LEGACY}\region\{REGION_EUROPE}\map\de_dust2")
    if csgo:
        seen = {}
        for s in csgo:
            k = f"appid={s.get('appid')} product={s.get('product')} gamedir={s.get('gamedir')}"
            seen[k] = seen.get(k, 0) + 1
        for k, v in sorted(seen.items(), key=lambda kv: -kv[1]):
            print(f"    {v:>6}  {k}")

    # 7. How much bigger is the world than Europe? Both hit the cap at full
    #    size, so measure the ratio on a partition small enough to be complete.
    print("\n  world vs Europe, sized on a partition that fits under the cap:")
    run("CS2 / world / de_dust2", key, rf"\appid\{CS2}\region\{REGION_WORLD}\map\de_dust2")

    # 8. V10's real question is POPULATION, and the raw count cannot answer it.
    #    Run 2 found >10 000 CS:GO servers on de_dust2 in Europe alone, against
    #    3457 for CS2 on the same map and region. That is not a scene twice the
    #    size of CS2's - it is listing spam, and the sample row above (map
    #    `test0`, secure False, gametype `empty,insecure`) is what it looks like.
    #    So filter to servers that are VAC-secure AND have someone on them, and
    #    sum the players rather than counting rows.
    print("\n  V10 - population on servers that are secure AND occupied:")
    for name, appid in (("CS2", CS2), ("CS:GO legacy", CSGO_LEGACY)):
        population(f"{name} / Europe / occupied+secure", key,
                   rf"\appid\{appid}\region\{REGION_EUROPE}\empty\1\secure\1")

    # 9. V10 - break the cap. Every CS:GO partition above returned 10 000, so
    #    the population is unmeasured, not measured-and-large. Partition on the
    #    hostname instead, which the cap does not correlate with. ~36 calls.
    if "--sweep" in sys.argv:
        print("\n  V10 - CS:GO legacy past the 10 000 cap, by name partition:")
        # The probe name comes from step 6's rows, so the semantics check is free.
        sweep("CS:GO legacy / Europe / occupied+secure / a-z0-9", key,
              rf"\appid\{CSGO_LEGACY}\region\{REGION_EUROPE}\empty\1\secure\1",
              known_name=str(csgo[0].get("name", "")) if csgo else None)
    else:
        print("\n  (V10 needs a name-partition sweep, ~36 calls: re-run with --sweep)")

    print("\n  Reminders:")
    print("   * there is NO country filter. Poland means GeoIP over these addresses.")
    print("   * a response is CAPPED AT 10 000 ROWS and does not say so. Any")
    print("     partition returning exactly the cap is incomplete - split it again.")
    print("   * 15-min polling is 96 calls/day per query; the documented key ceiling")
    print("     is far above that, so TIMEOUTS are the constraint, not call volume.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
