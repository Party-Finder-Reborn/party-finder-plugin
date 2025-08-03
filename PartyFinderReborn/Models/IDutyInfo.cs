using Lumina.Excel.Sheets;

namespace PartyFinderReborn.Models;

public interface IDutyInfo
{
    uint RowId { get; }
    string NameText { get; }
    uint ContentTypeId { get; }
    byte ClassJobLevelRequired { get; }
    ushort ItemLevelRequired { get; }
    bool HighEndDuty { get; }
    ushort TerritoryTypeId { get; }
}

public class RealDutyInfo : IDutyInfo
{
    public readonly ContentFinderCondition _contentFinderCondition;

    public RealDutyInfo(ContentFinderCondition contentFinderCondition)
    {
        _contentFinderCondition = contentFinderCondition;
    }

    public uint RowId => _contentFinderCondition.RowId;
    public string NameText => _contentFinderCondition.Name.ExtractText();
    public uint ContentTypeId => _contentFinderCondition.ContentType.RowId;
    public byte ClassJobLevelRequired => _contentFinderCondition.ClassJobLevelRequired;
    public ushort ItemLevelRequired => _contentFinderCondition.ItemLevelRequired;
    public bool HighEndDuty => _contentFinderCondition.HighEndDuty;
    public ushort TerritoryTypeId => (ushort)_contentFinderCondition.TerritoryType.RowId;
}

public class CustomDutyInfo : IDutyInfo
{
    public uint RowId { get; set; }
    public string NameText { get; set; } = string.Empty;
    public uint ContentTypeId { get; set; }
    public byte ClassJobLevelRequired { get; set; }
    public ushort ItemLevelRequired { get; set; }
    public bool HighEndDuty { get; set; }
    public ushort TerritoryTypeId { get; set; }
}
