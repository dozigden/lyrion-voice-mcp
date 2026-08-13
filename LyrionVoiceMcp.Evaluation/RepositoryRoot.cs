namespace LyrionVoiceMcp.Evaluation;

public static class RepositoryRoot
{
    public static string? Find(string startPath)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(startPath));
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LyrionVoiceMcp.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
