using System.Text;

using CRC.Helpers;
using CRC.Models;

using Microsoft.Extensions.Configuration;

Encoding.RegisterProvider( CodePagesEncodingProvider.Instance );

TextWriter? protocolWriter = null;

try {
    var configuration = new ConfigurationBuilder()
        .AddCommandLine( args, new Dictionary<string, string> {
            { "-d", "Target" },
            { "--dir", "Target" },
            { "-f", "Target" },
            { "--file", "Target" },

            { "-o", "ProtocolDir" },
            { "--out-protocol", "ProtocolDir" },
            { "-p", "ProtocolFile" },
            { "--protocol", "ProtocolFile" },
            { "-c", "CrcFile" },
            { "--crc", "CrcFile" },

            { "-s", "SilentMode" },
            { "--silent-mode", "SilentMode" },
            { "-bc", "BackwardCompatibility" },
            { "--backward-compatibility", "BackwardCompatibility" }
        } )
        .Build();

    AppArguments appArguments = new AppArguments();
    configuration.Bind( appArguments );
    
    if(string.IsNullOrWhiteSpace( appArguments.Target )) {
        if(appArguments.SilentMode)
            return 1;

        Console.WriteLine( "Введите путь к файлу или каталогу:" );
        appArguments.Target = Console.ReadLine() ?? string.Empty;

        if(string.IsNullOrWhiteSpace( appArguments.Target )) {
            throw new Exception( "Ошибка: Путь к файлу или каталогу не указан" );
        }
        
        Console.WriteLine( "Support Backward compatibility?" );
        appArguments.BackwardCompatibility = Console.ReadKey().Key == ConsoleKey.Y;
    }

    var attr = File.GetAttributes(appArguments.Target);
    var rootDir = Path.GetDirectoryName(appArguments.Target);
    if (rootDir is null) {
        if (appArguments.SilentMode)
            return 1;

        throw new Exception( "Не удалось получить корневой каталог" );
    }

    Console.WriteLine($"Запущен процесс подсчета контрольных сумм...");
    List<FileCrcInfo> fileCrcInfos = [];
    if(attr.HasFlag( FileAttributes.Directory )) {
        rootDir = appArguments.Target;
        var fileCrcList = ProcessDirectory(rootDir, appArguments.Target, appArguments.SilentMode, appArguments.BackwardCompatibility );

        fileCrcInfos.AddRange(fileCrcList);
    } else {
        var fileCrc = ProcessFile(rootDir, appArguments.Target, appArguments.SilentMode, appArguments.BackwardCompatibility );
        if(fileCrc is not null)
            fileCrcInfos.Add( fileCrc );
    }

    Encoding fileEncoding = appArguments.BackwardCompatibility ? GetAnsiEncoding() : new UTF8Encoding(false);
    string protocolDir = string.IsNullOrWhiteSpace(appArguments.ProtocolDir) ? rootDir : appArguments.ProtocolDir;
    if ( !string.IsNullOrWhiteSpace( appArguments.ProtocolDir ) && !Directory.Exists(protocolDir))
        Directory.CreateDirectory(protocolDir);

    string protocolPath = Path.Combine(protocolDir, appArguments.ProtocolFile);
    protocolWriter = new StreamWriter( protocolPath, false, fileEncoding);
    foreach(var file in fileCrcInfos) {
        protocolWriter.WriteLine( $"{file.ProtocolPath}={file.Crc}" );
    }
    protocolWriter.Close();

    if (appArguments.BackwardCompatibility) {
        Console.WriteLine("Заменяем переводы строк для обратной совместимости");
        string content = File.ReadAllText(protocolPath, fileEncoding);
        string normalizeEndings = content
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Replace("\n", "\r\n");

        Console.WriteLine( "Заменяем разделитель пути для обратной совместимости" );
        string normalizePathSeparators = normalizeEndings
            .Replace("/", "\\");

        File.WriteAllText( protocolPath, normalizePathSeparators, fileEncoding );
    }

    var protocolCrc = ProcessFile(protocolDir, protocolPath, appArguments.SilentMode, appArguments.BackwardCompatibility );
    if (protocolCrc is null) {
        if(appArguments.SilentMode)
            return 1;

        throw new Exception( $"Не удалось получить CRC для файла '{protocolPath}'");
    }

    string crcPath = Path.Combine(protocolDir, appArguments.CrcFile);
    File.WriteAllText( crcPath, protocolCrc.Crc, fileEncoding);

    Console.WriteLine( $"Процесс подсчета контрольных сумм завершен" );
    Console.WriteLine( $"Файл протокола: {protocolPath}" );
    Console.WriteLine( $"Файл c CRC: {crcPath}" );

    return 0;
} catch(Exception ex) {
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine( $"Error: {ex.Message}" );
    Console.ResetColor();
    return 1;
} finally {
    protocolWriter?.Dispose();
}

FileCrcInfo? ProcessFile(string rootDir, string fileRelativePath, bool inSilentMode, bool backwardCompatibility ) {
    string filePath = Path.Combine(rootDir, fileRelativePath);
    FileInfo fileInfo = new FileInfo( filePath );
    if(!fileInfo.Exists) {
        if(inSilentMode)
            return null;

        throw new FileNotFoundException( $"Файл '{filePath}' не найден" );
    }

    using FileStream fs = fileInfo.OpenRead();
    var crc = CRCHelper.CalcCrc32(fs).ToString("x8").ToUpper();

    string protocolFilePath = backwardCompatibility 
        ? Path.Combine( Path.GetDirectoryName(fileRelativePath)?.ToUpper() ?? string.Empty, fileInfo.Name.ToLower())
        : fileRelativePath;

    return new FileCrcInfo { FileName = fileInfo.Name, FilePath = fileRelativePath, ProtocolPath = protocolFilePath, Crc = crc };
}

IList<FileCrcInfo> ProcessDirectory( string rootDir, string dirPath, bool inSilentMode, bool backwardCompatibility ) {
    List<FileCrcInfo> fileCrcs = [];
    DirectoryInfo dirInfo = new DirectoryInfo(dirPath);
    
    if(!dirInfo.Exists) {
        if(inSilentMode)
            return fileCrcs;

        throw new DirectoryNotFoundException( $"Каталог '{dirPath}' не найден" );
    }

    foreach(var filePath in DirectoryHelper.GetFilesRecursive( rootDir )) {

        var file = ProcessFile(rootDir, filePath, inSilentMode, backwardCompatibility );
        if (file is not null)
            fileCrcs.Add( file );
    }

    return fileCrcs;
}

Encoding GetAnsiEncoding() {
    try {
        try {
            // Для Windows используем Windows-1251 (кириллица)
            return Encoding.GetEncoding( 1251 );
        } catch {
            // Резервный вариант для Windows
            return Encoding.GetEncoding( "Windows-1251" );
        }
    } catch(Exception) { 
        return Encoding.UTF8;
    }
}