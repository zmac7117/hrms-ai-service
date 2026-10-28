namespace HrmsAi.Resume.Settings;

public class StorageSettings
{
    public const string SectionName = "Storage";

    public string ResumesPath { get; set; } = @"C:\HRMS AI\data\resumes";
    public int MaxFileSizeMb { get; set; } = 10;
}

