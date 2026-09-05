using SpeedtestDashboard.Core.Providers;

namespace SpeedtestDashboard.Infrastructure.Providers.Ookla;

public sealed class OoklaServerListParser
{
    public IReadOnlyList<SpeedTestServer> Parse(string output, int maximumServers)
    {
        if (maximumServers is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumServers));
        }

        var lines = output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var headerIndex = Array.FindIndex(lines, line =>
            line.Contains("ID", StringComparison.Ordinal) &&
            line.Contains("Name", StringComparison.Ordinal) &&
            line.Contains("Location", StringComparison.Ordinal) &&
            line.Contains("Country", StringComparison.Ordinal));
        if (headerIndex < 0 || headerIndex + 1 >= lines.Length)
        {
            throw new OoklaOutputException("Ookla server output did not contain the expected table header.");
        }

        var header = lines[headerIndex];
        var idIndex = header.IndexOf("ID", StringComparison.Ordinal);
        var nameIndex = header.IndexOf("Name", StringComparison.Ordinal);
        var locationIndex = header.IndexOf("Location", StringComparison.Ordinal);
        var countryIndex = header.IndexOf("Country", StringComparison.Ordinal);
        if (idIndex < 0 || nameIndex <= idIndex || locationIndex <= nameIndex || countryIndex <= locationIndex)
        {
            throw new OoklaOutputException("Ookla server output contained an invalid table layout.");
        }

        var separatorIndex = headerIndex + 1;
        if (!lines[separatorIndex].Trim().All(character => character == '=') || lines[separatorIndex].Trim().Length < 8)
        {
            throw new OoklaOutputException("Ookla server output did not contain the expected table separator.");
        }

        var servers = new List<SpeedTestServer>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rawLine in lines.Skip(separatorIndex + 1))
        {
            if (servers.Count >= maximumServers)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            var trimmedLine = rawLine.TrimStart();
            var idEnd = trimmedLine.IndexOfAny([' ', '\t']);
            var id = idEnd < 0 ? trimmedLine : trimmedLine[..idEnd];
            var name = Slice(rawLine, nameIndex, locationIndex).Trim();
            var location = Slice(rawLine, locationIndex, countryIndex).Trim();
            var country = Slice(rawLine, countryIndex, rawLine.Length).Trim();
            if (!OoklaServerId.IsValid(id) || !IsValidText(name) || !IsValidText(location) || !IsValidText(country))
            {
                throw new OoklaOutputException("Ookla server output contained a malformed server row.");
            }

            if (!seen.Add(id))
            {
                continue;
            }

            servers.Add(new SpeedTestServer(
                ProviderId.Ookla,
                id,
                name,
                Sponsor: name,
                Location: $"{location}, {country}",
                CountryCode: null,
                Host: null,
                DistanceKilometres: null,
                LatencyMilliseconds: null));
        }

        return servers;
    }

    private static string Slice(string value, int start, int end)
    {
        if (start >= value.Length)
        {
            return string.Empty;
        }

        return value[start..Math.Min(end, value.Length)];
    }

    private static bool IsValidText(string value) =>
        value.Length is > 0 and <= 160 && !value.Any(char.IsControl);
}
