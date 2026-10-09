namespace SGCM.Test.Common.Database;

internal static class RepositoryPaths
{
    public static string Root { get; } = FindRoot();

    public static string Database => Path.Combine(Root, "database");

    public static string TestDatabase => Path.Combine(Root, "SGCM.Test", "Common", "Database");

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SGCM.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("No se encontró la raíz del repositorio (SGCM.sln).");
    }
}