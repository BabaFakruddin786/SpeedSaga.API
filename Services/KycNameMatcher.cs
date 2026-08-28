using System.Text.RegularExpressions;

namespace SpeedSaga.API.Services;

public static class KycNameMatcher
{
    public static bool NamesMatch(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        var na = Normalize(a);
        var nb = Normalize(b);
        if (na == nb) return true;

        var aFirst = na.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var bFirst = nb.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return !string.IsNullOrEmpty(aFirst) && aFirst == bFirst;
    }

    public static bool PanMatchesHolderInitial(string pan, string? name)
    {
        if (string.IsNullOrWhiteSpace(pan) || pan.Length < 4 || string.IsNullOrWhiteSpace(name))
            return false;

        var panInitial = char.ToUpperInvariant(pan[3]);
        foreach (var part in Normalize(name).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Length > 0 && part[0] == panInitial)
                return true;
        }
        return false;
    }

    public static string Normalize(string name) =>
        Regex.Replace(name.Trim().ToUpperInvariant(), @"\s+", " ");
}
