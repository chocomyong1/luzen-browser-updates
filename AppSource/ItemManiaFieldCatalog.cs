using System;

namespace LuzenBrowserReplica;

// UI category contract based on the user-supplied original registration screenshots.
// These are broad categories ONLY, not an invented game-specific item taxonomy.
internal static class ItemManiaFieldCatalog
{
    internal static readonly string[] Categories = { "머니", "아이템", "계정", "기타" };
    internal static readonly string[] TradingMethods = { "일반", "분할" };

    internal static string? CanonicalCategory(string? value) => value switch
    {
        "게임머니" => "머니", // spelling found in public references; retain the original UI label
        "머니" or "아이템" or "계정" or "기타" => value,
        _ => null
    };

    internal static bool IsCategory(string? value) => CanonicalCategory(value) is not null;
    internal static bool IsTradingMethod(string? value) => value is "일반" or "분할";
}
