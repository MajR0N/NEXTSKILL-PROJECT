/*
 * nextskill_probe.sma — answers V2b, V2c, V3, V4 and V15's CS 1.6 half in ONE
 * server session, because server time with real non-Steam clients is the scarce
 * resource, not code.
 *
 * V2b  Is setinfo readable AT CONNECT, early enough to gate team joining?
 * V2c  Does cl_filterstuffcmd 1 interfere with setinfo or with record?
 * V3   Does setinfo behave identically on NON-STEAM clients (Reunion/dproto)?
 * V4   Does the info-key length limit allow a high-entropy token?
 * V15  Can the server start a client-side POV demo recording?
 *
 * Build:  amxxpc nextskill_probe.sma
 * Run:    put nextskill_probe.amxx in addons/amxmodx/plugins + plugins.ini
 * Read:   addons/amxmodx/logs/nextskill_probe.log  (one CSV line per event)
 *
 * TESTERS: each tester connects TWICE — once with cl_filterstuffcmd 0, once
 * with 1 — and on both a Steam and a non-Steam client if possible. Say
 * "amx_probe_note <text>" in console to stamp the log with which run it is.
 *
 * ponytail: logs facts, decides nothing. Ceiling: it cannot see whether the
 * client actually began recording — the engine gives the server no such
 * feedback — so V15 needs a human watching their own screen. That is what
 * RESULTS.md is for. Upgrade path: none worth building; a human is cheaper.
 */

#include <amxmodx>

#define PLUGIN  "NEXTSKILL probe"
#define VERSION "1.0"
#define AUTHOR  "NEXTSKILL"

// V4: the token we actually intend to ship is 256-bit. Base64url of 32 bytes is
// 43 chars. Probe with that exact shape, not a round number, or the answer is
// about a different string than the one we will use.
#define PROBE_TOKEN "AAAABBBBCCCCDDDDEEEEFFFFGGGGHHHHIIIIJJJJKKK"
#define TOKEN_KEY   "_ns_probe"

new g_log[64];

public plugin_init()
{
    register_plugin(PLUGIN, VERSION, AUTHOR);
    register_clientcmd("amx_probe_note", "cmd_note");
    register_clientcmd("amx_probe_now",  "cmd_read_now");
}

public plugin_precache()
{
    // Build the log path once; get_time is cheap but the path never changes.
    new stamp[32];
    get_time("%Y%m%d", stamp, charsmax(stamp));
    formatex(g_log, charsmax(g_log), "nextskill_probe_%s.log", stamp);
}

Probe_Log(const fmt[], any:...)
{
    new msg[512];
    vformat(msg, charsmax(msg), fmt, 2);
    log_to_file(g_log, "%s", msg);
}

/* Escape commas so the log stays parseable when a player name contains one. */
Probe_Clean(const src[], dest[], len)
{
    copy(dest, len, src);
    for (new i = 0; dest[i]; i++)
        if (dest[i] == ',') dest[i] = ' ';
}

/*
 * V2b — the whole question. If get_user_info returns the value HERE, the token
 * is readable before the player can pick a team, and the roster gate can fail
 * closed. If it is empty here but populated later, 005's design needs rework.
 */
public client_connect(id)
{
    new steam[40], ip[24], name[64], safeName[64];
    get_user_authid(id, steam, charsmax(steam));
    get_user_ip(id, ip, charsmax(ip), 1);
    get_user_name(id, name, charsmax(name));
    Probe_Clean(name, safeName, charsmax(safeName));

    new setinfoVal[128];
    get_user_info(id, TOKEN_KEY, setinfoVal, charsmax(setinfoVal));

    new filterVal[8];
    get_user_info(id, "cl_filterstuffcmd", filterVal, charsmax(filterVal));

    // V3: a non-Steam client typically reports STEAM_ID_LAN / VALVE_ID_LAN / "ID_PENDING".
    // Recorded raw rather than classified — classifying it here would hide the
    // very variety we are trying to observe.
    Probe_Log("CONNECT,%s,%s,%s,setinfo_at_connect=[%s],len=%d,filter=[%s]",
              safeName, steam, ip, setinfoVal, strlen(setinfoVal), filterVal);

    return PLUGIN_CONTINUE;
}

/*
 * V2b (second half) — the same read once the client is fully in. If CONNECT was
 * empty and this is populated, we know the gate must wait, and by how long.
 *
 * V2c + V15 — issue both commands the design depends on and log that we tried.
 * Whether they LANDED is the tester's job to observe; see RESULTS.md.
 */
public client_putinserver(id)
{
    if (is_user_bot(id) || is_user_hltv(id)) return;
    set_task(1.0, "probe_stage_two", id);
}

public probe_stage_two(id)
{
    if (!is_user_connected(id)) return;

    new steam[40], name[64], safeName[64];
    get_user_authid(id, steam, charsmax(steam));
    get_user_name(id, name, charsmax(name));
    Probe_Clean(name, safeName, charsmax(safeName));

    new setinfoVal[128];
    get_user_info(id, TOKEN_KEY, setinfoVal, charsmax(setinfoVal));

    new filterVal[8];
    get_user_info(id, "cl_filterstuffcmd", filterVal, charsmax(filterVal));

    Probe_Log("INSERVER,%s,%s,setinfo_now=[%s],len=%d,filter=[%s]",
              safeName, steam, setinfoVal, strlen(setinfoVal), filterVal);

    // V2c/V4 — ask the client to set a full-length token, then read it back.
    // If the readback is truncated, V4 has its answer. If it is empty while the
    // filter is on, V2c has its answer.
    client_cmd(id, "setinfo %s %s", TOKEN_KEY, PROBE_TOKEN);
    set_task(2.0, "probe_readback", id);

    // V15 — ask for a POV demo. The tester watches their own client.
    client_cmd(id, "record nextskill_probe_%d", id);
    Probe_Log("RECORD_ISSUED,%s,%s,expected_file=nextskill_probe_%d.dem", safeName, steam, id);
}

public probe_readback(id)
{
    if (!is_user_connected(id)) return;

    new steam[40], name[64], safeName[64];
    get_user_authid(id, steam, charsmax(steam));
    get_user_name(id, name, charsmax(name));
    Probe_Clean(name, safeName, charsmax(safeName));

    new got[128];
    get_user_info(id, TOKEN_KEY, got, charsmax(got));

    new sent = strlen(PROBE_TOKEN);
    new back = strlen(got);
    new bool:exact = equal(got, PROBE_TOKEN);

    // The three outcomes that mean different things:
    //   back == 0        -> the client refused the stuffcmd  (V2c: filter blocks setinfo)
    //   back < sent      -> the info key was truncated       (V4: token too long)
    //   exact            -> both fine
    Probe_Log("READBACK,%s,%s,sent_len=%d,got_len=%d,exact=%d,got=[%s]",
              safeName, steam, sent, back, exact ? 1 : 0, got);
}

public cmd_note(id)
{
    new note[128], safeNote[128], name[64], safeName[64];
    read_args(note, charsmax(note));
    remove_quotes(note);
    Probe_Clean(note, safeNote, charsmax(safeNote));
    get_user_name(id, name, charsmax(name));
    Probe_Clean(name, safeName, charsmax(safeName));

    Probe_Log("NOTE,%s,%s", safeName, safeNote);
    client_print(id, print_console, "[probe] noted: %s", safeNote);
    return PLUGIN_HANDLED;
}

/* Lets a tester re-read their own state at any moment without reconnecting. */
public cmd_read_now(id)
{
    new setinfoVal[128], filterVal[8], name[64], safeName[64];
    get_user_info(id, TOKEN_KEY, setinfoVal, charsmax(setinfoVal));
    get_user_info(id, "cl_filterstuffcmd", filterVal, charsmax(filterVal));
    get_user_name(id, name, charsmax(name));
    Probe_Clean(name, safeName, charsmax(safeName));

    Probe_Log("MANUAL,%s,setinfo=[%s],len=%d,filter=[%s]",
              safeName, setinfoVal, strlen(setinfoVal), filterVal);
    client_print(id, print_console, "[probe] setinfo=[%s] len=%d filter=[%s]",
                 setinfoVal, strlen(setinfoVal), filterVal);
    return PLUGIN_HANDLED;
}
