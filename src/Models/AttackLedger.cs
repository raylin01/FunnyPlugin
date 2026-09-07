namespace Funnies.Models;

// Contains no engine objects. Serial-bearing handles prevent slot reuse from
// transferring penalties; callers must also Cancel on death (CS2 can reuse pawns).
public sealed class AttackLedger
{
    public sealed class Attempt(uint pawn, uint source, int tick, int damage, bool persistent)
    {
        public uint Pawn { get; } = pawn;
        public uint Source { get; } = source;
        public int Tick { get; } = tick;
        public int Damage { get; } = damage;
        public bool Hit { get; set; }
        public int? FinishTick { get; set; } = persistent ? null : tick + 2;
    }

    private readonly List<Attempt> _attempts = [];
    private readonly Dictionary<(uint Pawn, uint Source), int> _hits = [];
    public IReadOnlyList<Attempt> Attempts => _attempts;

    public Attempt Record(uint pawn, uint source, int tick, int damage, bool persistent = false)
    {
        // weapon_fire and the knife cooldown observation may describe the same swing.
        var duplicate = _attempts.LastOrDefault(a => a.Pawn == pawn && a.Source == source &&
            (persistent || Math.Abs(a.Tick - tick) <= 1));
        if (duplicate != null) return duplicate;
        var attempt = new Attempt(pawn, source, tick, damage, persistent);
        if (_hits.TryGetValue((pawn, source), out var hitTick) && Math.Abs(hitTick - tick) <= 1)
            attempt.Hit = true;
        _attempts.Add(attempt);
        return attempt;
    }

    public void Hit(uint pawn, uint source, int tick, bool persistent = false)
    {
        _hits[(pawn, source)] = tick;
        var attempt = _attempts.LastOrDefault(a => a.Pawn == pawn && a.Source == source &&
            (persistent || Math.Abs(a.Tick - tick) <= 1));
        if (attempt != null) attempt.Hit = true;
    }

    public List<Attempt> Collect(int tick)
    {
        var finished = _attempts.Where(a => a.FinishTick is { } end && end <= tick).ToList();
        foreach (var attempt in finished) _attempts.Remove(attempt);
        foreach (var key in _hits.Where(h => tick - h.Value > 2).Select(h => h.Key).ToList()) _hits.Remove(key);
        return finished;
    }

    public void Cancel(uint pawn)
    {
        _attempts.RemoveAll(a => a.Pawn == pawn);
        foreach (var key in _hits.Keys.Where(k => k.Pawn == pawn).ToList()) _hits.Remove(key);
    }

    public void Clear() { _attempts.Clear(); _hits.Clear(); }
}
