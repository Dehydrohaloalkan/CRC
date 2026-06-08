namespace CRC.Helpers;
internal class DirectoryHelper {
    public static IEnumerable<string> GetFilesRecursive( string rootPath ) {
        DirectoryInfo rootDir = new DirectoryInfo(rootPath);
        return GetFilesRecursive( rootDir );
    }

    private static IEnumerable<string> GetFilesRecursive(DirectoryInfo currentDir, string relativePath = "") {
        List<string> result = [];

        foreach( var subDir in currentDir.GetDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)) {
            string subRelativePath = string.IsNullOrWhiteSpace(relativePath) 
                ? subDir.Name
                : Path.Combine(relativePath, subDir.Name);

            result.AddRange( GetFilesRecursive( subDir, subRelativePath ) );
        }

        foreach( var file in currentDir.GetFiles().OrderBy( f => f.Name, StringComparer.OrdinalIgnoreCase) ) {
            string filePath = string.IsNullOrWhiteSpace(relativePath)
                ? file.Name
                : Path.Combine(relativePath, file.Name);

            result.Add( filePath );
        }

        return result;
    }
}