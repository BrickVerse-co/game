namespace Polytoria.Shared.Settings;

public class SettingSectionDef
{
	public required string Key { get; init; }
	public required string Label { get; init; }
	public string IconPath { get; init; } = string.Empty;
	public int SortOrder { get; init; } = 0;
}
