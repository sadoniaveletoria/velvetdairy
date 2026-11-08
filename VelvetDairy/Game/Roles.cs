using System.Collections.Generic;
using System.Linq;

namespace VelvetDairy.Game;

/// <summary>Members can hold several roles at once (cow, farmer, bull). Helpers for showing and checking them.</summary>
public static class Roles
{
    public static readonly string[] All = { "cow", "farmer", "bull" };

    public static List<string> Clean(IEnumerable<string>? roles) => All.Where(r => roles?.Contains(r) == true).ToList();

    /// <summary>"Cow + Farmer", or "No role".</summary>
    public static string Text(IEnumerable<string>? roles)
    {
        var list = Clean(roles);
        return list.Count == 0 ? "No role" : string.Join(" + ", list.Select(Title));
    }

    public static string Title(string r) => r.Length == 0 ? r : char.ToUpperInvariant(r[0]) + r[1..];

    /// <summary>Roles from a reply: the list when there is one, otherwise the single older "role" field.</summary>
    public static List<string> From(List<string>? list, string? single) =>
        list is { Count: > 0 } ? Clean(list) : string.IsNullOrEmpty(single) || single == "none" ? new List<string>() : Clean(new[] { single });
}
