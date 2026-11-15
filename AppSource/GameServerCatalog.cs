using System;
using System.Collections.Generic;
using System.Linq;

namespace LuzenBrowserReplica;

/// <summary>
/// User-facing display names only. ItemMania site category IDs are dynamic,
/// and must be matched against the live site's visible options during a workflow.
/// Never invent IDs or silently substitute another server.
/// </summary>
internal static class GameServerCatalog
{
    public const string LostArk = "로스트아크";

    private static readonly IReadOnlyDictionary<string, string[]> ServersByGame =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [LostArk] = new[]
            {
                "루페온", "실리안", "아만", "카마인",
                "카제로스", "아브렐슈드", "카단", "니나브"
            }
        };

    public static IReadOnlyList<string> Games => ServersByGame.Keys.ToArray();
    public static IReadOnlyList<string> Servers(string? game)
        => game is not null && ServersByGame.TryGetValue(game, out var servers)
            ? servers : Array.Empty<string>();

    public static bool IsSupported(string? game, string? server)
        => Servers(game).Contains(server ?? "", StringComparer.OrdinalIgnoreCase);

    public static string? CanonicalServer(string? game, string? server)
        => Servers(game).FirstOrDefault(s => s.Equals(server?.Trim(), StringComparison.OrdinalIgnoreCase));
}
