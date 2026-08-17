using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NextSkill.Contracts;

namespace NextSkill.Simulator;

/// <summary>Ways the simulator can deliberately behave badly.</summary>
[Flags]
public enum Misbehaviour
{
    None = 0,
    /// <summary>Sign with the wrong secret.</summary>
    BadSignature = 1,
    /// <summary>Use a timestamp far outside the replay window.</summary>
    StaleTimestamp = 2,
    /// <summary>Claim a contract version the panel does not support.</summary>
    WrongContract = 4,
    /// <summary>Send no signature headers at all.</summary>
    NoSignature = 8,
}

/// <summary>
/// Speaks the real wire contract over real HTTP (decision 038).
///
/// <b>Not a stub.</b> A stub returns canned answers and teaches you nothing; a
/// simulator plays the real role over the real protocol, so the day the actual
/// plugin arrives the API has already been exercised by something that behaves
/// like one. Crucially it can misbehave, which is the only way to prove the
/// integrity gates in decision 035 actually fire.
/// </summary>
public sealed class PluginSimulator(HttpClient http, Guid serverId, string secret)
{
    private long _seq;

    /// <summary>Next sequence number, so callers can create gaps deliberately.</summary>
    public long NextSeq() => ++_seq;

    public void SkipSeq(int count) => _seq += count;

    public Task<HttpResponseMessage> Heartbeat(string pluginVersion = "0.1.0", Misbehaviour bad = Misbehaviour.None)
    {
        var body = new HeartbeatRequest
        {
            Contract = WireContract.Version,
            ServerId = serverId.ToString(),
            PluginVersion = pluginVersion,
            Game = WireGame.Cs2,
        };
        return Post($"/api/v1/servers/{serverId}/heartbeat", NextSkillJson.Serialize(body), bad);
    }

    public Task<HttpResponseMessage> GetConfig(Guid matchId, Misbehaviour bad = Misbehaviour.None) =>
        Send(HttpMethod.Get, $"/api/v1/servers/{serverId}/matches/{matchId}/config", "", bad);

    public Task<HttpResponseMessage> MatchLoaded(Guid matchId, long seq,
        Misbehaviour bad = Misbehaviour.None) =>
        SendEvent(matchId, seq, MatchEventType.MatchLoaded, new { }, bad);

    /// <summary>
    /// A player presenting both binding factors (decision 011).
    ///
    /// The simulator can present a wrong one on purpose, which is the whole
    /// reason this exists before the plugin does: 034 says the simulator covers
    /// flow, integrity, security and identity, and identity was the one word in
    /// that sentence with nothing behind it.
    /// </summary>
    public Task<HttpResponseMessage> PlayerBound(
        Guid matchId, long seq, Guid accountId, string setinfoToken, string linkCode,
        string slot = "player", Misbehaviour bad = Misbehaviour.None)
    {
        var payload = new PlayerBoundPayload
        {
            Slot = slot,
            AccountId = accountId.ToString(),
            InGameId = "STEAM_1:0:12345",
            SetinfoToken = setinfoToken,
            LinkCode = linkCode,
        };
        return SendEvent(matchId, seq, MatchEventType.PlayerBound, payload, bad);
    }

    public Task<HttpResponseMessage> RoundEnd(Guid matchId, long seq, int round, int scoreA, int scoreB,
        Misbehaviour bad = Misbehaviour.None)
    {
        var payload = new RoundEndPayload
        {
            Round = round, ScoreA = scoreA, ScoreB = scoreB,
            WinnerSide = scoreA > scoreB ? WireSide.Ct : WireSide.T,
        };
        return SendEvent(matchId, seq, MatchEventType.RoundEnd, payload, bad);
    }

    /// <summary>
    /// Ends the map. Pass <paramref name="reportedA"/>/<paramref name="reportedB"/>
    /// different from the accumulated round scores to simulate the authoritative
    /// report disagreeing — which must produce <c>disputed</c>.
    /// </summary>
    public Task<HttpResponseMessage> MapEnd(Guid matchId, long seq, string map, int reportedA, int reportedB,
        int roundsPlayed, Misbehaviour bad = Misbehaviour.None)
    {
        var payload = new MapEndPayload
        {
            Map = map, MapNumber = 0,
            FinalScoreA = reportedA, FinalScoreB = reportedB,
            RoundsPlayed = roundsPlayed,
            Players = [],
            Authoritative = true,
        };
        return SendEvent(matchId, seq, MatchEventType.MapEnd, payload, bad);
    }

    private Task<HttpResponseMessage> SendEvent<T>(Guid matchId, long seq, MatchEventType type, T payload, Misbehaviour bad)
    {
        var envelope = new MatchEvent
        {
            Contract = WireContract.Version,
            MatchId = matchId.ToString(),
            Seq = seq,
            SentAt = DateTimeOffset.UtcNow,
            Type = type,
            Payload = JsonSerializer.SerializeToElement(payload, NextSkillJson.Options),
        };
        return Post($"/api/v1/servers/{serverId}/matches/{matchId}/events", NextSkillJson.Serialize(envelope), bad);
    }

    private Task<HttpResponseMessage> Post(string path, string json, Misbehaviour bad) =>
        Send(HttpMethod.Post, path, json, bad);

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string json, Misbehaviour bad)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var request = new HttpRequestMessage(method, path);

        if (body.Length > 0)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        var contract = bad.HasFlag(Misbehaviour.WrongContract) ? 99 : WireContract.Version;
        request.Headers.Add(WireContract.VersionHeader, contract.ToString());

        if (!bad.HasFlag(Misbehaviour.NoSignature))
        {
            // A stale timestamp is signed correctly — the point is that a genuine
            // signature must still be refused once it is old enough to be a replay.
            var ts = bad.HasFlag(Misbehaviour.StaleTimestamp)
                ? DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeSeconds()
                : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var signingSecret = bad.HasFlag(Misbehaviour.BadSignature) ? "not-the-right-secret" : secret;

            request.Headers.Add(WireContract.TimestampHeader, ts.ToString());
            request.Headers.Add(WireContract.SignatureHeader, RequestSignature.Compute(body, ts, signingSecret));
        }

        return await http.SendAsync(request);
    }
}
