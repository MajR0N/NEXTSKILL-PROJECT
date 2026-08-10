using System.Text.Json;
using NextSkill.Contracts;

namespace NextSkill.Checks;

/// <summary>
/// Wire-contract checks — decision 035, layer 1: <b>make malformed loud</b>.
///
/// A shared DTO library catches shape drift at compile time. These checks cover
/// what it cannot: what happens at runtime when the JSON is wrong.
/// </summary>
public static class ContractChecks
{
    public static void Run(Action<string, Action> check, Action<bool, string> that)
    {
        static MatchConfig SampleConfig() => new()
        {
            Contract = WireContract.Version,
            MatchId = "01J8X",
            Game = WireGame.Cs16,
            BestOf = 1,
            Maps = [new MapAssignment { Map = "de_dust2", TeamAStartsAs = WireSide.Ct }],
            TeamA = new TeamConfig
            {
                TeamId = "t-a", Name = "Pukawka Five",
                Roster = [new RosterEntry { AccountId = "acc-1", Nickname = "kubaa", Slot = "a1" }],
            },
            TeamB = new TeamConfig
            {
                TeamId = "t-b", Name = "Nokaut",
                Roster = [new RosterEntry { AccountId = "acc-2", Nickname = "rafcio", Slot = "b1" }],
            },
            EventsUrl = "https://nextskill.test/api/v1/servers/s1/matches/01J8X/events",
        };

        check("config round-trips through the shared serializer unchanged", () =>
        {
            var original = SampleConfig();
            var restored = NextSkillJson.Deserialize<MatchConfig>(NextSkillJson.Serialize(original));

            that(restored.MatchId == original.MatchId, "match id survived");
            that(restored.Maps[0].TeamAStartsAs == WireSide.Ct, "starting side survived");
            that(restored.TeamA.Roster[0].Slot == "a1", "roster slot survived");
            that(restored.KnifeRound == false, "knife round defaults off (decision 028)");
        });

        check("JSON is snake_case and enums are snake_case strings", () =>
        {
            var json = NextSkillJson.Serialize(SampleConfig());

            that(json.Contains("\"match_id\""), "property names must be snake_case");
            that(json.Contains("\"team_a_starts_as\""), "nested property names too");
            that(json.Contains("\"cs16\""), $"enums must serialise as readable strings, got: {json[..Math.Min(160, json.Length)]}");
            that(!json.Contains("\"MatchId\""), "PascalCase must not leak into the wire format");
        });

        check("UNKNOWN field is REJECTED, never silently ignored", () =>
        {
            // The tempting default is to skip unrecognised fields. That turns a
            // renamed field into silence instead of an error — and silence is
            // how a wrong score reaches a bracket (035).
            var json = """
            {"contract":1,"match_id":"01J8X","seq":1,"sent_at":"2026-08-10T18:22:31Z",
             "type":"round_end","payload":{},"surprise_field":"unexpected"}
            """;

            var rejected = false;
            try { _ = NextSkillJson.Deserialize<MatchEvent>(json); }
            catch (JsonException) { rejected = true; }

            that(rejected, "an unknown member MUST throw — ignoring it hides contract drift");
        });

        check("MISSING SCORE is rejected, NOT defaulted to zero", () =>
        {
            // The single most likely form of the silent-failure bug (035):
            // a renamed or dropped score field quietly becoming 0, and the
            // wrong team advancing.
            var json = """
            {"map":"de_dust2","map_number":0,"final_score_a":13,
             "rounds_played":24,"players":[],"authoritative":true}
            """;

            var rejected = false;
            try { _ = NextSkillJson.Deserialize<MapEndPayload>(json); }
            catch (JsonException) { rejected = true; }

            that(rejected, "a missing final_score_b MUST throw. Defaulting it to 0 is the bug 035 exists to prevent");
        });

        check("a complete map_end payload deserialises correctly", () =>
        {
            var json = """
            {"map":"de_dust2","map_number":0,"final_score_a":13,"final_score_b":11,
             "rounds_played":24,
             "players":[{"slot":"a1","account_id":"acc-1","kills":22,"deaths":17,"assists":4}],
             "authoritative":true}
            """;

            var payload = NextSkillJson.Deserialize<MapEndPayload>(json);
            that(payload.FinalScoreA == 13 && payload.FinalScoreB == 11, "scores read correctly");
            that(payload.Authoritative, "authoritative flag must be explicit, not inferred from the event name");
            that(payload.Players[0].Kills == 22, "per-player stats read correctly");
        });

        check("event payload is read with the same strict rules as the envelope", () =>
        {
            var json = """
            {"contract":1,"match_id":"01J8X","seq":42,"sent_at":"2026-08-10T18:22:31Z",
             "type":"round_end",
             "payload":{"round":24,"score_a":13,"score_b":11,"winner_side":"ct","extra":"nope"}}
            """;

            var evt = NextSkillJson.Deserialize<MatchEvent>(json);
            that(evt.Seq == 42, "envelope parsed");

            var rejected = false;
            try { _ = evt.PayloadAs<RoundEndPayload>(); }
            catch (JsonException) { rejected = true; }

            that(rejected, "strictness must reach INSIDE the payload, not stop at the envelope");
        });

        check("a contract version we do not know is detectable before parsing further", () =>
        {
            var json = """
            {"contract":99,"match_id":"01J8X","seq":1,"sent_at":"2026-08-10T18:22:31Z",
             "type":"ready","payload":{}}
            """;

            var evt = NextSkillJson.Deserialize<MatchEvent>(json);
            that(evt.Contract != WireContract.Version,
                "an unknown contract version must be visible so the panel can reject it loudly rather than best-effort parse");
        });

        check("the config carries DECIDED maps and sides — no veto rules on the wire", () =>
        {
            // Veto happens on the web (028), which is why neither plugin needs a
            // veto state machine. If a map pool ever appears in this type, that
            // decision has been quietly reversed.
            var properties = typeof(MatchConfig).GetProperties().Select(p => p.Name).ToArray();
            that(!properties.Any(n => n.Contains("Pool", StringComparison.OrdinalIgnoreCase)),
                "MatchConfig must not carry a map pool — veto is decided before the server is contacted");
            that(!properties.Any(n => n.Contains("Veto", StringComparison.OrdinalIgnoreCase)),
                "MatchConfig must not carry veto rules");
            that(properties.Contains("Maps"), "it must carry the decided map order");
        });
    }
}
