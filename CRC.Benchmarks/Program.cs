using System.Diagnostics;

using CRC.Helpers;

// ------------------------------------------------------------------
// Бенчмарк вариантов CRC.
//
//   dotnet run -c Release                      # 100 МБ случайных данных, 3 прохода
//   dotnet run -c Release -- --size 200        # 200 МБ
//   dotnet run -c Release -- --iter 5          # 5 проходов
//   dotnet run -c Release -- --file C:\path\big.dat   # реальный файл
//   dotnet run -c Release -- --no-bitwise      # пропустить медленный эталон
//
// ВАЖНО: запускать только в Release (-c Release), иначе цифры бессмысленны.
// ------------------------------------------------------------------

int sizeMB = 100;
int iterations = 3;
string? file = null;
bool runBitwise = true;

for (int i = 0; i < args.Length; i++) {
    switch (args[i]) {
        case "--size" or "-s": sizeMB = int.Parse(args[++i]); break;
        case "--iter" or "-i": iterations = int.Parse(args[++i]); break;
        case "--file" or "-f": file = args[++i]; break;
        case "--no-bitwise": runBitwise = false; break;
        default: Console.WriteLine($"Неизвестный аргумент: {args[i]}"); return 1;
    }
}

byte[] data;
string source;
if (file is not null) {
    if (!File.Exists(file)) { Console.WriteLine($"Файл не найден: {file}"); return 1; }
    data = File.ReadAllBytes(file);
    source = $"файл {Path.GetFileName(file)}";
} else {
    data = new byte[(long)sizeMB * 1024 * 1024 is var n && n <= int.MaxValue ? (int)n : throw new Exception("Слишком большой размер для одного массива (>2 ГБ)")];
    new Random(42).NextBytes(data);
    source = $"{sizeMB} МБ случайных данных";
}

double mb = data.Length / (1024.0 * 1024.0);
Console.WriteLine($"Данные: {source} ({mb:F1} МБ), проходов: {iterations}");
Console.WriteLine($"Сборка: {(IsDebug() ? "DEBUG (!) цифры некорректны, нужен -c Release" : "Release")}");
Console.WriteLine();

// MustMatch=false у версии Z: это стандартный zlib-CRC, он ОБЯЗАН отличаться.
var variants = new List<(string Name, Func<byte[], uint> Fn, bool MustMatch)> {
    ("v2 (две таблицы)", d => CRCHelper.CalcCrc32V2(d), true),
    ("v3 (ручной s8)",   d => CRCHelper.CalcCrc32V3(d), true),
    ("v4 (ручной s16)",  d => CRCHelper.CalcCrc32V4(d), true),
    // кривая «ширина → скорость», обобщённый slice-by-N:
    ("slice-N=2",        d => CRCHelper.CalcCrc32SliceN(d, 2),  true),
    ("slice-N=4",        d => CRCHelper.CalcCrc32SliceN(d, 4),  true),
    ("slice-N=8",        d => CRCHelper.CalcCrc32SliceN(d, 8),  true),
    ("slice-N=16",       d => CRCHelper.CalcCrc32SliceN(d, 16), true),
    ("slice-N=32",       d => CRCHelper.CalcCrc32SliceN(d, 32), true),
    ("z (lib, zlib)",    d => CRCHelper.CalcCrc32Z(d),  false),
};
if (runBitwise)
    variants.Insert(0, ("v1 (побитово)", d => CRCHelper.CalcCrc32V1(d), true));

// --- проверка корректности ---
// эталон — побитовый v1 (если он не отключён, иначе v3, чтобы не ждать 5 с зря)
uint reference = runBitwise ? CRCHelper.CalcCrc32V1(data) : CRCHelper.CalcCrc32V3(data);
Console.WriteLine($"Эталонная сумма (наш CRC): {reference:X8}");
var results = new Dictionary<string, uint>();
foreach (var (name, fn, mustMatch) in variants) {
    uint v = fn(data);
    results[name] = v;
    if (mustMatch && v != reference) {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  РАСХОЖДЕНИЕ в '{name}': {v:X8} != {reference:X8}");
        Console.ResetColor();
        return 1;
    }
}
Console.WriteLine("Версии v1/v2/v3 совпадают; z отличается — так и должно быть.\n");

// --- замеры ---
Console.WriteLine($"{"вариант",-18} {"CRC",10} {"лучшее, мс",12} {"среднее, мс",12} {"МБ/с",10} {"ускорение",11}");
Console.WriteLine(new string('-', 77));

double? baseline = null;
foreach (var (name, fn, mustMatch) in variants) {
    fn(data); // прогрев + JIT

    double best = double.MaxValue, sum = 0;
    for (int it = 0; it < iterations; it++) {
        var sw = Stopwatch.StartNew();
        fn(data);
        sw.Stop();
        double ms = sw.Elapsed.TotalMilliseconds;
        best = Math.Min(best, ms);
        sum += ms;
    }
    double avg = sum / iterations;
    double mbps = mb / (best / 1000.0);
    baseline ??= best;
    string crc = results[name].ToString("X8") + (mustMatch ? "" : "*");
    Console.WriteLine($"{name,-18} {crc,10} {best,12:F1} {avg,12:F1} {mbps,10:F0} {baseline.Value / best,10:F1}x");
}
Console.WriteLine("\n* z — стандартный zlib-CRC-32, значение ДРУГОЕ; для протокола ЦБ не подходит.");

return 0;

static bool IsDebug() {
#if DEBUG
    return true;
#else
    return false;
#endif
}
