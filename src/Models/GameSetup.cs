namespace Funnies.Models;

public sealed class GameSetup
{
    public bool Enabled { get; set; }
    public bool Live { get; set; }
    public bool Overtime { get; set; } = true;
    public string SpecialEconomy { get; set; } = "full";
    public int SpecialTeam { get; set; } = 2;
    public Dictionary<string, string> Roles { get; set; } = new();
    public Dictionary<string, int> TeamOverrides { get; set; } = new();

    public int RoundGrant(int round, int regulationRounds) => SpecialEconomy == "full" &&
        round > 1 && round != regulationRounds / 2 + 1 ? 16000 : 0;

    public int TeamFor(string id) => TeamOverrides.TryGetValue(id, out var team)
        ? team : Roles.ContainsKey(id) ? SpecialTeam : SpecialTeam == 2 ? 3 : 2;
}
