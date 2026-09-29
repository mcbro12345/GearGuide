using System.Collections.Generic;

using GearGuide.Game;

namespace GearGuide.UI;

// A stat of the recommended set next to what's worn now.
internal readonly record struct StatLine(string Name, int Recommended, int Current);

// Everything the window shows, rebuilt whenever the plan changes.
internal sealed class GearGuideView
{
    public required JobProfile Profile { get; init; }
    public required GearPlan Plan { get; init; }
    public required IReadOnlyList<StatLine> Stats { get; init; }
}
