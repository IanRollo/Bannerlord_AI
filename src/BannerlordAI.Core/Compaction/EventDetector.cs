using System;
using System.Collections.Generic;
using BannerlordAI.Core.Model;

namespace BannerlordAI.Core.Compaction;

public enum EventSeverity
{
    Minor,
    /// <summary>Wakes the Commander early.</summary>
    Major,
}

public sealed class BattleEvent
{
    public double Time { get; init; }
    public EventSeverity Severity { get; init; }
    public string Text { get; init; } = "";

    public override string ToString() => Wire.Clock(Time) + " " + Text;
}

public sealed class EventDetectorOptions
{
    /// <summary>Report an enemy formation once it has moved this far since its last reported position.</summary>
    public float MoveReportDistance { get; init; } = 100f;
    /// <summary>Report when an enemy mounted formation comes within this distance of an own ranged formation.</summary>
    public float CavalryThreatDistance { get; init; } = 120f;
}

/// <summary>
/// Diffs successive snapshots into a short log of notable changes. Deterministic and cheap; this is how context is
/// compacted between Commander calls instead of re-sending history.
/// </summary>
public sealed class EventDetector
{
    private sealed class Track
    {
        public Vec2 ReportedPosition;
        public int StrengthBucket;
        public bool WasEngaged;
        public bool WasRouting;
        public bool WasAlive;
    }

    private readonly EventDetectorOptions _options;
    private readonly Dictionary<string, Track> _tracks = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _threatPairs = new(StringComparer.OrdinalIgnoreCase);
    private bool _contact;
    private bool _initialised;

    public EventDetector(EventDetectorOptions? options = null)
    {
        _options = options ?? new EventDetectorOptions();
    }

    public bool FirstContactHappened => _contact;

    public List<BattleEvent> Update(BattleSnapshot s)
    {
        var events = new List<BattleEvent>();
        var firstUpdate = !_initialised;
        _initialised = true;

        foreach (var f in s.All)
        {
            if (!_tracks.TryGetValue(f.Id, out var t))
            {
                _tracks[f.Id] = new Track
                {
                    ReportedPosition = f.Position,
                    StrengthBucket = Bucket(f.Strength),
                    WasEngaged = f.IsEngaged,
                    WasRouting = f.IsRouting,
                    WasAlive = f.IsAlive,
                };
                if (!firstUpdate && f.IsAlive)
                    events.Add(Ev(s, EventSeverity.Minor, $"{Name(f)} appeared at {s.Frame.ToLocal(f.Position)}"));
                continue;
            }

            var own = f.Allegiance == Allegiance.Own;

            if (t.WasAlive && !f.IsAlive)
            {
                events.Add(Ev(s, own ? EventSeverity.Major : EventSeverity.Minor, $"{Name(f)} destroyed"));
                t.WasAlive = false;
                continue;
            }
            if (!f.IsAlive) continue;

            if (f.IsRouting && !t.WasRouting)
                events.Add(Ev(s, own ? EventSeverity.Major : EventSeverity.Minor, $"{Name(f)} is ROUTING"));
            else if (!f.IsRouting && t.WasRouting)
                events.Add(Ev(s, EventSeverity.Minor, $"{Name(f)} rallied"));
            t.WasRouting = f.IsRouting;

            if (f.IsEngaged && !t.WasEngaged)
                events.Add(Ev(s, EventSeverity.Minor, $"{Name(f)} entered melee"));
            t.WasEngaged = f.IsEngaged;

            var bucket = Bucket(f.Strength);
            if (bucket < t.StrengthBucket)
            {
                var major = own && bucket <= 2 && t.StrengthBucket > 2; // crossed 50%
                events.Add(Ev(s, major ? EventSeverity.Major : EventSeverity.Minor,
                    $"{Name(f)} down to {Wire.F(f.Strength * 100)}% ({f.Count}/{f.InitialCount})"));
                t.StrengthBucket = bucket;
            }

            if (f.Allegiance == Allegiance.Enemy)
            {
                var moved = f.Position - t.ReportedPosition;
                if (moved.Length >= _options.MoveReportDistance)
                {
                    var dir = SnapshotFormatter.DirectionLabel(s.Frame.DirToLocal(moved));
                    events.Add(Ev(s, EventSeverity.Minor,
                        $"{Name(f)} moved {Wire.F(moved.Length)}m {DirectionPhrase(dir)}, now at {s.Frame.ToLocal(f.Position)}"));
                    t.ReportedPosition = f.Position;
                }
            }
        }

        if (!_contact)
        {
            foreach (var f in s.Own)
            {
                if (f.IsAlive && f.IsEngaged)
                {
                    _contact = true;
                    events.Add(Ev(s, EventSeverity.Major, "FIRST CONTACT: lines have met"));
                    break;
                }
            }
        }

        DetectCavalryThreats(s, events);
        return events;
    }

    private void DetectCavalryThreats(BattleSnapshot s, List<BattleEvent> events)
    {
        foreach (var mine in s.Own)
        {
            if (!mine.IsAlive || mine.Role != FormationRole.Ranged) continue;
            foreach (var e in s.Enemies)
            {
                if (!e.IsAlive || !Roles.IsMounted(e.Role)) continue;
                var key = e.Id + ">" + mine.Id;
                var close = Vec2.Distance(mine.Position, e.Position) <= _options.CavalryThreatDistance;
                if (close && _threatPairs.Add(key))
                    events.Add(Ev(s, EventSeverity.Minor, $"{Name(e)} within {Wire.F(_options.CavalryThreatDistance)}m of your {Name(mine)}"));
                else if (!close && Vec2.Distance(mine.Position, e.Position) > _options.CavalryThreatDistance * 1.5f)
                    _threatPairs.Remove(key);
            }
        }
    }

    private static string DirectionPhrase(string dir) => dir switch
    {
        "ahead" => "toward the enemy side",
        "back" => "toward your side",
        _ => "toward your " + dir,
    };

    private static int Bucket(float strength) => strength >= 0.75f ? 4 : strength >= 0.5f ? 3 : strength >= 0.25f ? 2 : 1;

    private static string Name(FormationState f) => f.Id + " (" + f.RoleName + ")";

    private static BattleEvent Ev(BattleSnapshot s, EventSeverity sev, string text) => new() { Time = s.Time, Severity = sev, Text = text };
}
