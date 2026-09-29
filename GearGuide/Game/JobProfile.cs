using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Player;
using Lumina.Excel.Sheets;

using GearGuide.Base;

namespace GearGuide.Game;

// Who the gear is for: job, level and the character details that restrict
// what can be worn, plus how much each stat is worth to that job.
internal sealed class JobProfile
{
    // ClassJobCategory rows for the four disciplines.
    private const uint DisciplesOfLand = 32;
    private const uint DisciplesOfHand = 33;
    private const byte TankRole = 1;
    private const byte HealerRole = 4;

    // BaseParam ids.
    private const byte Vitality = 3;
    private const byte Intelligence = 4;
    private const byte Mind = 5;
    private const byte Piety = 6;
    private const byte Gp = 10;
    private const byte Cp = 11;
    private const byte Tenacity = 19;
    private const byte DirectHit = 22;
    private const byte CriticalHit = 27;
    private const byte Determination = 44;
    private const byte SkillSpeed = 45;
    private const byte SpellSpeed = 46;
    private const byte Craftsmanship = 70;
    private const byte Control = 71;
    private const byte Gathering = 72;
    private const byte Perception = 73;

    private readonly HashSet<uint> categories = new();
    private readonly Dictionary<byte, float> weights = new();

    public uint JobId { get; }
    public string JobName { get; }
    public string JobAbbreviation { get; }
    public int Level { get; }
    public byte Race { get; }
    public bool Female { get; }
    public uint GrandCompany { get; }

    public JobProfile(uint jobId, int level, byte race, bool female, uint grandCompany)
    {
        JobId = jobId;
        Level = level;
        Race = race;
        Female = female;
        GrandCompany = grandCompany;

        var job = Services.DataManager.GetExcelSheet<ClassJob>().GetRow(jobId);
        JobName = job.Name.ToString();
        JobAbbreviation = job.Abbreviation.ToString();

        // ClassJobCategory has one boolean column per class/job, named by its
        // abbreviation, so the categories this job belongs to are the rows with
        // that column set.
        var column = typeof(ClassJobCategory).GetProperty(JobAbbreviation);
        if (column != null)
            foreach (var category in Services.DataManager.GetExcelSheet<ClassJobCategory>())
                if (column.GetValue(category) is true) categories.Add(category.RowId);

        BuildWeights(job);
    }

    // The stats this job cares about, most important first.
    public IEnumerable<byte> KeyStats
        => weights.Where(pair => pair.Value >= 0.25f).OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key).Select(pair => pair.Key);

    public bool CanWear(GearItem item)
    {
        if (item.LevelEquip > Level || !categories.Contains(item.ClassJobCategory)) return false;
        if (item.GrandCompany != 0 && item.GrandCompany != GrandCompany) return false;
        return item.EquipRace <= 1 || RaceAllows(item.EquipRace);
    }

    // How good a piece is for this job. Stats are weighted by how much the job
    // wants them; defence and item level only separate otherwise equal pieces.
    public float Score(GearItem item, bool hq)
    {
        float score = 0.0f;
        foreach (var (param, weight) in weights)
            score += weight * item.Stat(param, hq);
        score += 0.02f * (item.Stat(GearItem.DefenseParam, hq) + item.Stat(GearItem.MagicDefenseParam, hq));
        return score + 0.001f * item.ItemLevel;
    }

    private void BuildWeights(ClassJob job)
    {
        switch (job.ClassJobCategory.RowId)
        {
            case DisciplesOfHand:
                weights[Craftsmanship] = 1.0f;
                weights[Control] = 1.0f;
                weights[Cp] = 1.5f;
                return;
            case DisciplesOfLand:
                weights[Gathering] = 1.0f;
                weights[Perception] = 1.0f;
                weights[Gp] = 1.5f;
                return;
        }

        // Combat jobs: the main stat dominates, weapon damage matters most of
        // all on a weapon, and secondary stats are worth about a third.
        byte mainStat = job.PrimaryStat;
        weights[mainStat] = 1.0f;
        bool caster = mainStat is Intelligence or Mind;
        weights[caster ? GearItem.MagicDamageParam : GearItem.PhysicalDamageParam] = 8.0f;
        weights[Vitality] = job.Role == TankRole ? 0.4f : 0.1f;
        foreach (byte secondary in new[] { CriticalHit, DirectHit, Determination })
            weights[secondary] = 0.3f;
        weights[caster ? SpellSpeed : SkillSpeed] = 0.25f;
        if (job.Role == TankRole) weights[Tenacity] = 0.25f;
        if (job.Role == HealerRole) weights[Piety] = 0.15f;
    }

    private bool RaceAllows(uint restriction)
    {
        if (!Services.DataManager.GetExcelSheet<EquipRaceCategory>().TryGetRow(restriction, out var row)) return true;
        if (Female ? !row.Female : !row.Male) return false;
        return Race switch
        {
            1 => row.Hyur,
            2 => row.Elezen,
            3 => row.Lalafell,
            4 => row.Miqote,
            5 => row.Roegadyn,
            6 => row.AuRa,
            7 => row.Hrothgar,
            8 => row.Viera,
            _ => true,
        };
    }

    // The local player's profile, or `current` again if nothing has changed.
    public static JobProfile? ForLocalPlayer(JobProfile? current)
    {
        var state = Services.PlayerState;
        if (!state.IsLoaded || state.ClassJob.RowId == 0) return current;
        uint jobId = state.ClassJob.RowId;
        byte race = (byte)state.Race.RowId;
        bool female = state.Sex == Sex.Female;
        uint grandCompany = state.GrandCompany.RowId;
        if (current != null && current.JobId == jobId && current.Level == state.Level && current.Race == race
            && current.Female == female && current.GrandCompany == grandCompany)
            return current;
        return new JobProfile(jobId, state.Level, race, female, grandCompany);
    }
}
