using System.Buffers;
using System.Collections.Concurrent;
using System.Numerics;

namespace CRC.Helpers;

// =====================================================================================
//  CRC-32 в конвенции ЦБ РФ (АРМ КБР). Три эквивалентные реализации: V1, V2, V3.
//
//  ОБЩАЯ МАТЕМАТИКА
//  ----------------
//  CRC трактует поток байт как многочлен над полем GF(2) — это биты, где сложение и
//  вычитание совпадают и равны XOR (нет переносов). Контрольная сумма — это остаток от
//  деления (дополненного) многочлена сообщения на образующий многочлен G(x) степени 32:
//
//      x^32 + x^26 + x^23 + x^22 + x^16 + x^12 + x^11 + x^10 + x^8 + x^7 + x^5 + x^4 + x^2 + x + 1
//
//  Реализация «отражённая» (reflected): биты обрабатываются младшим вперёд (LSB-first),
//  регистр сдвигается ВПРАВО, а G(x) хранится в перевёрнутом виде как константа
//  POLY = 0xEDB88320 (это битовый разворот «прямого» 0x04C11DB7).
//
//  Особенность ИМЕННО этой конвенции (важно — это НЕ обычный zlib-CRC-32):
//    • биты данных вдвигаются СВЕРХУ (в бит 31), обратная связь снимается СНИЗУ (бит 0);
//    • в конце — «дофлаш» 32 нулевых бит (дополнение сообщения 32 нулями) и инверсия.
//  Контрольное значение: "123456789" -> 0x22896B0A (у zlib было бы 0xCBF43926).
//
//  КЛЮЧЕВОЙ ФАКТ, на котором стоят V2 и V3: обновление состояния ЛИНЕЙНО над GF(2).
//  То есть для шага обработки f выполняется суперпозиция:  f(a XOR b) = f(a) XOR f(b).
//  Поэтому побитовые циклы можно заменить заранее посчитанными таблицами — результат
//  бит-в-бит тот же. Все таблицы ниже строятся из побитового Step8, так что совпадение
//  V1 == V2 == V3 гарантировано by construction.
// =====================================================================================
public static class CRCHelper {
    private const uint POLY = 0xEDB88320;   // отражённый образующий многочлен
    private const uint INIT = 0xFFFFFFFF;   // начальное состояние регистра (флаг fl=1: старт с -1)

    // Ts[low]  — вклад МЛАДШЕГО байта состояния за один байт-шаг (обратная связь снизу).
    // Tb[byte] — вклад входного байта за один байт-шаг (вдвигается сверху).
    private static readonly uint[] Ts = new uint[256];
    private static readonly uint[] Tb = new uint[256];

    // Таблицы slice-by-8:
    // Slice[j][b]    — вклад байта на позиции j внутри 8-байтового блока,
    // StateMix[k][s] — вклад k-го байта старого состояния, протянутого через 8 байт-шагов.
    private static readonly uint[][] Slice = new uint[8][];
    private static readonly uint[][] StateMix = new uint[4][];

    // Таблицы slice-by-16 (для V4): та же логика, но ширина блока 16, поэтому M^16 и M^(15-j).
    private static readonly uint[][] Slice16 = new uint[16][];
    private static readonly uint[][] StateMix16 = new uint[4][];

    // Оператор M как матрица 32x32 над GF(2) (для V5/CombineState). ByteMat[i] = M(1<<i) —
    // образ i-го базисного вектора при распространении состояния на ОДИН байт. Этого хватает,
    // чтобы матрично возводить M в любую степень L и применять M^L к 32-битному состоянию за
    // O(32 log L) вместо O(L) проходов M — то есть «склеивать» независимо посчитанные куски.
    private static readonly uint[] ByteMat = new uint[32];

    static CRCHelper() {
        for (int i = 0; i < 256; i++) {
            Ts[i] = Step8((uint)i, 0);   // состояние = i, входной байт = 0
            Tb[i] = Step8(0, i);         // состояние = 0, входной байт = i
        }
        for (int j = 0; j < 8; j++) {
            Slice[j] = new uint[256];
            for (int b = 0; b < 256; b++)
                Slice[j][b] = Mk(Tb[b], 7 - j);          // M^(7-j) от вклада байта
        }
        for (int k = 0; k < 4; k++) {
            StateMix[k] = new uint[256];
            for (int sb = 0; sb < 256; sb++)
                StateMix[k][sb] = Mk((uint)sb << (8 * k), 8); // M^8 от байта состояния на его позиции
        }
        for (int j = 0; j < 16; j++) {
            Slice16[j] = new uint[256];
            for (int b = 0; b < 256; b++)
                Slice16[j][b] = Mk(Tb[b], 15 - j);       // M^(15-j) от вклада байта
        }
        for (int k = 0; k < 4; k++) {
            StateMix16[k] = new uint[256];
            for (int sb = 0; sb < 256; sb++)
                StateMix16[k][sb] = Mk((uint)sb << (8 * k), 16); // M^16 от байта состояния
        }
        for (int i = 0; i < 32; i++)
            ByteMat[i] = M(1u << i);                              // столбцы матрицы M (после заполнения Ts)
    }

    // =================================================================================
    //  V1 — ПОБИТОВО (LFSR / деление «в столбик»). Эталон.
    //  --------------------------------------------------------------------------------
    //  Прямая реализация деления многочленов. Регистр fcs — текущий остаток. Для каждого
    //  байта 8 раз:
    //    1) смотрим выходной бит (младший, fcs & 1) и сдвигаем регистр вправо;
    //    2) вдвигаем один бит данных в старший разряд (бит 31);
    //    3) если выходной бит был 1 — XOR с образующим POLY (в GF(2) «вычесть» = XOR).
    //  Это форма Галуа для LFSR-деления. Завершающий цикл на 32 шага (Finish) — это
    //  дополнение сообщения 32 нулевыми битами, проталкивающее хвост через делитель,
    //  а финальный XOR с 0xFFFFFFFF — стандартная инверсия выхода CRC-32.
    //  Стоимость: 8 ветвлений+сдвигов на байт — отсюда и медленность.
    // =================================================================================
    public static uint CalcCrc32V1(ReadOnlySpan<byte> data) {
        uint fcs = INIT;
        foreach (byte b in data)
            fcs = Step8(fcs, b);
        return Finish(fcs);
    }

    // =================================================================================
    //  V2 — ПОБАЙТОВО, ДВЕ ТАБЛИЦЫ.
    //  --------------------------------------------------------------------------------
    //  Шаг обработки одного байта T(state, byte) — это композиция 8 битовых шагов, и она
    //  линейна над GF(2). По суперпозиции вклад состояния и вклад байта независимы:
    //
    //        T(state, byte) = T(state, 0) XOR T(0, byte)
    //
    //  Вклад состояния T(state,0) дополнительно расщепляется. За 8 сдвигов регистр уезжает
    //  на байт вниз, поэтому:
    //
    //        T(state, 0) = (state >> 8) XOR Ts[state & 0xFF]
    //
    //    • (state >> 8): старшие 24 бита просто сдвигаются — за 8 шагов обратная связь до
    //      них не доходит;
    //    • Ts[state & 0xFF]: вся обратная связь порождается ТОЛЬКО младшим байтом (лишь его
    //      биты успевают доехать до бита 0 и включить XOR с POLY) ⇒ хватает таблицы на 256.
    //
    //  Итог — один байт за пару обращений к памяти, без внутреннего цикла:
    //
    //        fcs = (fcs >> 8) XOR Ts[fcs & 0xFF] XOR Tb[byte]
    //
    //  Почему ДВЕ таблицы, а в учебном zlib-CRC одна? Там байт сразу XOR-ится в МЛАДШИЕ
    //  биты (crc ^= byte), поэтому байт и обратная связь «встречаются» и сворачиваются в
    //  одну индексируемую таблицу T[(crc ^ byte) & 0xFF]. Здесь же байт входит СВЕРХУ, а
    //  обратная связь — СНИЗУ: за один байт-шаг они не пересекаются, и вклады остаются
    //  раздельными ⇒ две таблицы (Ts к тому же не биекция, свернуть в одну нельзя).
    // =================================================================================
    public static uint CalcCrc32V2(ReadOnlySpan<byte> data) {
        uint fcs = INIT;
        foreach (byte b in data)
            fcs = (fcs >> 8) ^ Ts[fcs & 0xFF] ^ Tb[b];
        return Finish(fcs);
    }

    // =================================================================================
    //  V3 — SLICE-BY-8 (8 байт за итерацию).
    //  --------------------------------------------------------------------------------
    //  Цель — убрать последовательную зависимость V2 (каждый шаг ждёт предыдущий fcs),
    //  чтобы процессор считал обращения к таблицам параллельно (ILP) и прятал задержки
    //  памяти.
    //
    //  Обозначим «чистое» распространение состояния за один байт (без входных данных):
    //
    //        M(x) = (x >> 8) XOR Ts[x & 0xFF]
    //
    //  тогда полный шаг с данными это  x' = M(x) XOR Tb[byte].
    //  Развернём блок из 8 байт b0..b7 от состояния s, пользуясь линейностью M
    //  (M(a XOR b) = M(a) XOR M(b)):
    //
    //        state_8 = M^8(s)  XOR  Σ_{j=0..7}  M^(7-j)( Tb[b_j] )
    //
    //  Смысл: байт b_j вбрасывается на шаге j как Tb[b_j], после чего к нему применяются
    //  оставшиеся (7-j) шагов M; исходное состояние s проходит через M восемь раз.
    //  Предвычисляем:
    //    • Slice[j][b]    = M^(7-j)(Tb[b])         — 8 таблиц, по одной на позицию в блоке;
    //    • StateMix[k][s] = M^8(s_k << 8k)         — раскладка M^8(s) по 4 байтам состояния
    //      (M^8 линейна ⇒ M^8(s) = XOR по байтам).
    //
    //  Все 12 обращений (4 за состояние + 8 за байты) НЕзависимы между собой, поэтому
    //  конвейеризуются ⇒ ~2x к V2 и ~60x к V1, упираясь уже в пропускную способность памяти.
    // =================================================================================
    public static uint CalcCrc32V3(ReadOnlySpan<byte> data) => Finish(V3Core(INIT, data));

    // =================================================================================
    //  V4 — SLICE-BY-16, РУЧНОЙ РАЗВОРОТ (16 байт за итерацию).
    //  --------------------------------------------------------------------------------
    //  То же, что V3, но ширина блока 16:
    //        state_16 = M^16(s)  XOR  Σ_{j=0..15}  M^(15-j)( Tb[b_j] )
    //  Таблицы: Slice16[16][256] (M^(15-j)) и StateMix16[4][256] (M^16).
    //  Цикл развёрнут вручную (как в V3) — это важно: обобщённый slice-N с циклом по j
    //  заметно медленнее из-за накладных расходов и хуже использует ILP.
    //
    //  Память таблиц: (16 + 4) * 256 * 4 = 20 КБ. Влезает в 32-КБ L1, но запас уже мал —
    //  на CPU с меньшим L1 выигрыш над V3 может исчезнуть. См. README, раздел про ширину.
    // =================================================================================
    public static uint CalcCrc32V4(ReadOnlySpan<byte> data) => Finish(V4Core(INIT, data));

    // =================================================================================
    //  Обобщённый slice-by-N с произвольной шириной (для исследования «ширина → скорость»).
    //  Математика та же, что у V3, но ширина блока W — параметр:
    //      state_W = M^W(s) ⊕ Σ_{j=0}^{W-1} M^(W-1-j)( Tb[b_j] )
    //  Таблицы под каждую ширину строятся один раз и кешируются. Реализация обобщённая
    //  (цикл по j вместо ручного разворота), поэтому она показывает ФОРМУ кривой, но в
    //  абсолюте может уступать ручному V3 на той же ширине 8.
    // =================================================================================
    private static readonly Dictionary<int, (uint[] StateMix, uint[] Slice)> _sliceCache = new();

    private static (uint[] StateMix, uint[] Slice) GetSliceTables(int w) {
        lock (_sliceCache) {
            if (_sliceCache.TryGetValue(w, out var t)) return t;
            var stateMix = new uint[4 * 256];           // [k*256 + sb] = M^w(sb << 8k)
            for (int k = 0; k < 4; k++)
                for (int sb = 0; sb < 256; sb++)
                    stateMix[k * 256 + sb] = Mk((uint)sb << (8 * k), w);
            var slice = new uint[w * 256];              // [j*256 + b] = M^(w-1-j)(Tb[b])
            for (int j = 0; j < w; j++)
                for (int b = 0; b < 256; b++)
                    slice[j * 256 + b] = Mk(Tb[b], w - 1 - j);
            t = (stateMix, slice);
            _sliceCache[w] = t;
            return t;
        }
    }

    public static uint CalcCrc32SliceN(ReadOnlySpan<byte> data, int width) {
        var (stateMix, slice) = GetSliceTables(width);
        uint s = INIT;
        int i = 0, lim = data.Length - data.Length % width;
        for (; i < lim; i += width) {
            uint acc = stateMix[s & 0xFF] ^ stateMix[256 + ((s >> 8) & 0xFF)]
                     ^ stateMix[512 + ((s >> 16) & 0xFF)] ^ stateMix[768 + ((s >> 24) & 0xFF)];
            for (int j = 0; j < width; j++)
                acc ^= slice[(j << 8) + data[i + j]];
            s = acc;
        }
        for (; i < data.Length; i++)
            s = (s >> 8) ^ Ts[s & 0xFF] ^ Tb[data[i]];
        return Finish(s);
    }

    // =================================================================================
    //  V5 — МНОГОПОТОЧНО + ПОТОКОВО (склейка независимых кусков).
    //  --------------------------------------------------------------------------------
    //  V1–V4 ускоряли ОДИН последовательный проход. V5 распараллеливает сам проход по
    //  ядрам, оставаясь бит-в-бит совместимым с протоколом ЦБ.
    //
    //  Ключ — та же линейность. Обработка L байт из состояния s раскладывается на вклад
    //  состояния и вклад данных, не зависящие друг от друга:
    //
    //        F(s, chunk) = M^L(s)  XOR  F(0, chunk)
    //
    //  Значит данные можно нарезать на куски и посчитать partial_i = F(0, chunk_i) НЕЗАВИСИМО
    //  в разных потоках (каждый стартует с нуля, V3Core). Затем — строго последовательная,
    //  но дешёвая «склейка» по порядку кусков:
    //
    //        s = INIT;  для каждого i:  s = M^{L_i}(s) XOR partial_i;   return Finish(s)
    //
    //  Дорогое здесь — M^{L_i} (L_i ~ миллионы байт), поэтому M возводится в степень не
    //  прогоном по байтам, а матрично над GF(2) за O(32 log L) (см. MatPow/ApplyOp). Все
    //  куски кроме последнего одной длины ⇒ M^L считается один раз и переиспользуется.
    //
    //  Корректность: склейка даёт ровно ту же последовательность состояний, что и сплошной
    //  проход V3 ⇒ V5 == V1..V4 при любом числе потоков и любой нарезке.
    // =================================================================================

    // Версия для готового массива в памяти: режем на ~равные части по числу ядер и считаем
    // их параллельно (Parallel.For), затем склеиваем. Мелкие данные — просто последовательно.
    public static uint CalcCrc32V5(byte[] data, int degreeOfParallelism = 0) {
        if (degreeOfParallelism <= 0) degreeOfParallelism = Environment.ProcessorCount;
        const int MinPerThread = 1 << 16;                 // мельчить нет смысла — накладные расходы съедят выигрыш
        int n = data.Length;
        int parts = Math.Clamp(n / MinPerThread, 1, degreeOfParallelism);
        if (parts <= 1)
            return Finish(V3Core(INIT, data));

        int baseLen = n / parts;
        var partial = new uint[parts];
        var lens = new int[parts];
        Parallel.For(0, parts, p => {
            int start = p * baseLen;
            int len = (p == parts - 1) ? n - start : baseLen;  // хвост достаётся последней части
            lens[p] = len;
            partial[p] = V3Core(0u, data.AsSpan(start, len)); // partial = F(0, chunk), без INIT
        });
        return CombineParts(partial, lens, parts);
    }

    // Потоковая версия: читатель сливает куски фиксированного размера из Stream в очередь,
    // пул воркеров считает их partial-суммы, в конце — склейка по порядку. Память ограничена
    // (в полёте максимум ~degreeOfParallelism буферов), поэтому подходит для файлов любого размера.
    public static uint CalcCrc32V5(Stream stream, int degreeOfParallelism = 0) {
        if (degreeOfParallelism <= 0) degreeOfParallelism = Environment.ProcessorCount;
        const int ChunkSize = 1 << 20;                    // 1 МБ на кусок

        var queue = new BlockingCollection<(int Idx, byte[] Buf, int Len)>(degreeOfParallelism * 2);
        var results = new ConcurrentDictionary<int, (uint Partial, int Len)>();

        var workers = new Task[degreeOfParallelism];
        for (int w = 0; w < degreeOfParallelism; w++) {
            workers[w] = Task.Run(() => {
                foreach (var item in queue.GetConsumingEnumerable()) {
                    uint p = V3Core(0u, item.Buf.AsSpan(0, item.Len));
                    results[item.Idx] = (p, item.Len);
                    ArrayPool<byte>.Shared.Return(item.Buf);
                }
            });
        }

        int idx = 0;
        while (true) {
            byte[] buf = ArrayPool<byte>.Shared.Rent(ChunkSize);
            int total = ReadFull(stream, buf, ChunkSize);
            if (total == 0) { ArrayPool<byte>.Shared.Return(buf); break; }
            queue.Add((idx++, buf, total));
            if (total < ChunkSize) break;                 // короткий кусок = конец потока
        }
        queue.CompleteAdding();
        Task.WaitAll(workers);

        int count = idx;
        var partial = new uint[count];
        var lens = new int[count];
        for (int i = 0; i < count; i++)
            (partial[i], lens[i]) = results[i];
        return CombineParts(partial, lens, count);
    }

    // Последовательная склейка partial-сумм кусков: s = M^{L_i}(s) XOR partial_i, затем Finish.
    private static uint CombineParts(uint[] partial, int[] lens, int count) {
        if (count == 0) return Finish(INIT);
        uint[] opBase = MatPow(ByteMat, lens[0]);         // длина общая для всех, кроме хвоста
        int baseLen = lens[0];
        uint s = INIT;
        for (int i = 0; i < count; i++) {
            uint[] op = (lens[i] == baseLen) ? opBase : MatPow(ByteMat, lens[i]);
            s = ApplyOp(op, s) ^ partial[i];
        }
        return Finish(s);
    }

    private static int ReadFull(Stream s, byte[] buf, int want) {
        int total = 0, read;
        while (total < want && (read = s.Read(buf, total, want - total)) > 0)
            total += read;
        return total;
    }

    // ---- арифметика операторов M над GF(2) (матрица 32x32, столбец = образ базисного вектора) ----

    // Применить оператор к состоянию: v = XOR столбцов mat по выставленным битам v.
    private static uint ApplyOp(uint[] mat, uint v) {
        uint r = 0;
        while (v != 0) {
            r ^= mat[BitOperations.TrailingZeroCount(v)];
            v &= v - 1;
        }
        return r;
    }

    // Композиция операторов (сначала b, потом a). Все степени одного M коммутируют.
    private static uint[] MatMul(uint[] a, uint[] b) {
        var c = new uint[32];
        for (int i = 0; i < 32; i++) c[i] = ApplyOp(a, b[i]);
        return c;
    }

    // M^bytes бинарным возведением в степень: O(32 log bytes) вместо bytes проходов M.
    private static uint[] MatPow(uint[] mat, long bytes) {
        var result = new uint[32];
        for (int i = 0; i < 32; i++) result[i] = 1u << i;  // единичная матрица
        var basePow = (uint[])mat.Clone();
        for (long e = bytes; e > 0; e >>= 1) {
            if ((e & 1) != 0) result = MatMul(basePow, result);
            basePow = MatMul(basePow, basePow);
        }
        return result;
    }

    // =================================================================================
    //  Z — СТАНДАРТНЫЙ CRC-32 ИЗ БИБЛИОТЕКИ (для сравнения, НЕ для протокола ЦБ!).
    //  --------------------------------------------------------------------------------
    //  System.IO.Hashing.Crc32 считает «обычный» CRC-32/ISO-HDLC (zlib, ZIP, PNG, Ethernet)
    //  и аппаратно ускорен в .NET. Тот же полином 0xEDB88320, но ДРУГАЯ конвенция: байт
    //  XOR-ится в младшие биты регистра (а не вдвигается сверху) и нет дофлаша 32 бит.
    //  Поэтому число ОТЛИЧАЕТСЯ: "123456789" -> 0xCBF43926 вместо нашего 0x22896B0A.
    //  Здесь только для замеров «потолка» и наглядной демонстрации расхождения — в
    //  протоколе использовать НЕЛЬЗЯ, приёмная сторона не сойдётся.
    // =================================================================================
    public static uint CalcCrc32Z(ReadOnlySpan<byte> data)
        => System.IO.Hashing.Crc32.HashToUInt32(data);

    // =================================================================================
    //  Производственный вход: потоково, на базе V3 (slice-by-8).
    // =================================================================================
    public static uint CalcCrc32(Stream stream) {
        uint fcs = INIT;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(1 << 20);
        try {
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                fcs = V3Core(fcs, buffer.AsSpan(0, read));
        } finally {
            ArrayPool<byte>.Shared.Return(buffer);
        }
        return Finish(fcs);
    }

    // ---------------------------------------------------------------------------------
    //  Внутренняя кухня.
    // ---------------------------------------------------------------------------------

    // Ядро V3: обрабатывает блоки по 8 байт, хвост (<8) — побайтово формулой V2.
    // Побайтовый и блочный пути двигают одно и то же состояние, поэтому стыки буферов
    // корректны при любой длине d.
    private static uint V3Core(uint s, ReadOnlySpan<byte> d) {
        int i = 0, lim = d.Length - (d.Length & 7);
        for (; i < lim; i += 8) {
            s = StateMix[0][s & 0xFF] ^ StateMix[1][(s >> 8) & 0xFF]
              ^ StateMix[2][(s >> 16) & 0xFF] ^ StateMix[3][(s >> 24) & 0xFF]
              ^ Slice[0][d[i]]     ^ Slice[1][d[i + 1]] ^ Slice[2][d[i + 2]] ^ Slice[3][d[i + 3]]
              ^ Slice[4][d[i + 4]] ^ Slice[5][d[i + 5]] ^ Slice[6][d[i + 6]] ^ Slice[7][d[i + 7]];
        }
        for (; i < d.Length; i++)
            s = (s >> 8) ^ Ts[s & 0xFF] ^ Tb[d[i]];
        return s;
    }

    // Ядро V4: блоки по 16 байт (ручной разворот), хвост — формулой V2.
    private static uint V4Core(uint s, ReadOnlySpan<byte> d) {
        int i = 0, lim = d.Length - (d.Length & 15);
        for (; i < lim; i += 16) {
            s = StateMix16[0][s & 0xFF] ^ StateMix16[1][(s >> 8) & 0xFF]
              ^ StateMix16[2][(s >> 16) & 0xFF] ^ StateMix16[3][(s >> 24) & 0xFF]
              ^ Slice16[0][d[i]]      ^ Slice16[1][d[i + 1]]  ^ Slice16[2][d[i + 2]]  ^ Slice16[3][d[i + 3]]
              ^ Slice16[4][d[i + 4]]  ^ Slice16[5][d[i + 5]]  ^ Slice16[6][d[i + 6]]  ^ Slice16[7][d[i + 7]]
              ^ Slice16[8][d[i + 8]]  ^ Slice16[9][d[i + 9]]  ^ Slice16[10][d[i + 10]] ^ Slice16[11][d[i + 11]]
              ^ Slice16[12][d[i + 12]] ^ Slice16[13][d[i + 13]] ^ Slice16[14][d[i + 14]] ^ Slice16[15][d[i + 15]];
        }
        for (; i < d.Length; i++)
            s = (s >> 8) ^ Ts[s & 0xFF] ^ Tb[d[i]];
        return s;
    }

    // Распространение состояния на один байт без входных данных.
    private static uint M(uint x) => (x >> 8) ^ Ts[x & 0xFF];
    private static uint Mk(uint x, int k) {
        for (int i = 0; i < k; i++) x = M(x);
        return x;
    }

    // 8 бит обработки — точная копия внутреннего цикла исходного Calc_crc32.
    private static uint Step8(uint fcs, int data) {
        for (int k = 0; k < 8; k++) {
            bool bit = (fcs & 1) != 0;
            fcs >>= 1;
            if ((data & 1) != 0) fcs |= 0x80000000u; else fcs &= 0x7FFFFFFFu;
            if (bit) fcs ^= POLY;
            data >>= 1;
        }
        return fcs;
    }

    // Завершение FCS (флаг fl=2): дофлаш 32 нулевых бит + инверсия выхода.
    private static uint Finish(uint fcs) {
        for (int k = 0; k < 32; k++) {
            bool bit = (fcs & 1) != 0;
            fcs >>= 1;
            fcs &= 0x7FFFFFFFu;
            if (bit) fcs ^= POLY;
        }
        return fcs ^ 0xFFFFFFFF;
    }
}
