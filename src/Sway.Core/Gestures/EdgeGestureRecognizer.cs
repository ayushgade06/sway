using Sway.Core.Input;

namespace Sway.Core.Gestures;

public enum Zone { None, Left, Right }

public enum GestureKind
{
    /// <summary>One finger touched down inside an enabled edge zone. Nothing changes yet.</summary>
    Candidate,
    Started,
    /// <summary><see cref="GestureEvent.Delta"/> is a signed fraction of the full range (+ = finger moved up).</summary>
    LevelDelta,
    Ended,
}

/// <summary>A value type so recognizing allocates nothing per frame.</summary>
public readonly record struct GestureEvent(GestureKind Kind, Zone Zone, float Delta = 0);

public enum GestureState { Idle, Candidate, Active, Rejected }

/// <summary>
/// Edge-swipe state machine (PRODUCT_SPEC §2.2). Pure: no I/O and no clock reads, so it runs identically on
/// live and recorded frames. Not thread-safe.
/// </summary>
public sealed class EdgeGestureRecognizer(Settings settings)
{
    const float JitterMm = 0.3f;
    const float VerticalDominance = 2f; // |dy| ≥ 2·|dx| ≈ within 27° of vertical

    readonly List<GestureEvent> _events = new(4);
    int _id;
    float _startX, _startY, _lastY;

    public Settings Settings { get; set; } = settings;
    public GestureState State { get; private set; }
    public Zone Zone { get; private set; }

    /// <summary>Forgets any touch in progress (the source stopped delivering frames).</summary>
    public void Reset() => (State, Zone) = (GestureState.Idle, Zone.None);

    /// <summary>Processes one frame. The returned list is reused by the next call; iterate it with an index.</summary>
    public List<GestureEvent> Process(TouchFrame frame)
    {
        _events.Clear();
        int touching = 0;
        Contact c = default;
        for (int i = 0; i < frame.Contacts.Count; i++)
        {
            var k = frame.Contacts[i];
            if (!k.Tip) continue;
            touching++;
            if (touching == 1 || k.Id == _id) c = k;
        }

        switch (State)
        {
            case GestureState.Idle:
                if (touching == 0) break;
                Zone = touching == 1 && c.Confident ? ZoneAt(c.Xmm, frame.WidthMm) : Zone.None;
                if (Zone == Zone.None) { State = GestureState.Rejected; break; }
                (_id, _startX, _startY, _lastY) = (c.Id, c.Xmm, c.Ymm, c.Ymm);
                State = GestureState.Candidate;
                _events.Add(new(GestureKind.Candidate, Zone));
                break;

            case GestureState.Candidate:
                if (touching == 0) { State = GestureState.Idle; break; } // a tap: never activates
                if (touching > 1 || c.Id != _id || !c.Confident) { State = GestureState.Rejected; break; }
                float dx = Math.Abs(c.Xmm - _startX), dy = Math.Abs(c.Ymm - _startY);
                if (Math.Max(dx, dy) < Settings.ActivationDistanceMm) break;
                if (dy < Settings.ActivationDistanceMm || dy < VerticalDominance * dx) { State = GestureState.Rejected; break; }
                State = GestureState.Active;
                _events.Add(new(GestureKind.Started, Zone));
                Move(c.Ymm); // the activation travel counts, so the change tracks the whole swipe
                break;

            case GestureState.Active:
                if (touching == 1 && c.Id == _id && c.Confident) { Move(c.Ymm); break; }
                _events.Add(new(GestureKind.Ended, Zone));
                // Lifted → Idle. A second finger or palm ends the gesture, then waits for every finger to lift.
                State = touching == 0 ? GestureState.Idle : GestureState.Rejected;
                break;

            case GestureState.Rejected:
                if (touching == 0) State = GestureState.Idle;
                break;
        }
        return _events;
    }

    void Move(float y)
    {
        float dy = y - _lastY;
        if (Math.Abs(dy) < JitterMm) return; // accumulates: slow movement still registers once it passes the filter
        _lastY = y;
        float mmPerRange = Action(Zone) == ZoneAction.Volume ? Settings.VolumeSensitivity : Settings.BrightnessSensitivity;
        _events.Add(new(GestureKind.LevelDelta, Zone, -dy / mmPerRange)); // Y grows downward; up = louder/brighter
    }

    Zone ZoneAt(float x, float width)
    {
        float edge = width * Settings.ZoneWidthPercent / 100f;
        var zone = x <= edge ? Zone.Left : x >= width - edge ? Zone.Right : Zone.None;
        return Action(zone) == ZoneAction.Off ? Zone.None : zone;
    }

    public ZoneAction Action(Zone zone) => zone switch
    {
        Zone.Left => Settings.LeftZone.Action,
        Zone.Right => Settings.RightZone.Action,
        _ => ZoneAction.Off,
    };
}
