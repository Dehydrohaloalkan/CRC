namespace CRC.Helpers;
public static class CRCHelper {
    public static uint CalcCrc32(Stream stream) {
        long iCrc32 = 0;

        var L = stream.Seek(0, SeekOrigin.End);
        _ = stream.Seek( 0, SeekOrigin.Begin );
        const long D = (100000 * 3 * (2 ^ 20));

        for(long S = 0; S < L; S += D) {
            if(L <= D) {
                var R1 = new byte[L];
                _ = stream.Read( R1, 0, (int)L );
                Calc_crc32( out iCrc32, iCrc32, R1, R1.Length, 3 );
                Array.Clear( R1, 0, R1.Length );
            } else if((S == 0) && (L > D)) {
                var R1 = new byte[D];
                _ = stream.Read( R1, 0, (int)D );
                Calc_crc32( out iCrc32, iCrc32, R1, R1.Length, 1 );
                Array.Clear( R1, 0, R1.Length );
            } else if((L - D) > S) {
                var R1 = new byte[D];
                _ = stream.Read( R1, 0, (int)D );
                Calc_crc32( out iCrc32, iCrc32, R1, R1.Length, 0 );
                Array.Clear( R1, 0, R1.Length );
            } else {
                var R1 = new byte[L - S];
                _ = stream.Read( R1, 0, (int)(L - S) );
                Calc_crc32( out iCrc32, iCrc32, R1, R1.Length, 2 );
                Array.Clear( R1, 0, R1.Length );
            }
        }
        _ = stream.Seek( 0, SeekOrigin.Begin );

        return (uint)iCrc32;
    }

    #region 
    /*==================================================================
    Вычислить контрольную сумму (32 бит) памяти
    ====================================================================
      Функция вычисляет контрольную сумму памяти CRC - это остаток
      от деления памяти (исходного полинома) на образующий полином 32-й
      степени:
      X**32 + X**26 + X**23 + X**22 + X**16 + X**12 + X**11 + X**10 +
      + X**8  + X**7  + X**5  + X**4  + X**2  + X + 1
      в соответствии с ISO 3309 или МККТТ X.25.

      Если память обрабатывается по блокам, то:
        - флаг fl=1 -для первого блока памяти (бит 0 флага = 1);
        - флаг fl=2 - для последнего блока памяти (бит 1 флага = 1);
        - флаг fl=0 - для средних блоков памяти.

      Если вся память обрабатывается как один блок, то флаг fl=3.

      Если память обрабатывается по блокам, то между обращениями к этой
      функции вызывающая функция не должна изменять содержимое crc32.

      Если вычислить CRC памяти, расположить CRC непосредственно за этой
      памятью: <____память____><младший_байт_CRC>...<старший_байт_CRC>
      и вычислить CRC совокупной строки, то эта CRC всегда будет равна
      0x2144Df1C независимо от содержимого и длины памяти.

    --------------------------------------------------------------------
      ВХОД:  crc32 - указатель на CRC
             mem - указатель на память
             ln  - длина памяти в байтах
             fl  - флаг
      ВЫХОД: Нет

    ==================================================================*/

    public static void Calc_crc32( out long crc32, long incrc32, byte[] mem, int ln, int fl ) {
        crc32 = incrc32;
        int i; /* Рабочие */

        /* Загрузить старую FCS */
        var fcs = (fl & 1) != 0 ? -1 : crc32;

        /* Вычислить новую FCS */
        for(i = 0; i < ln; i++) {
            var iii = mem[i]; /* Рабочие */
            int ii; /* Рабочие */
            for(ii = 0; ii < 8; ii++) {
                if((fcs & 1) != 0) {
                    fcs >>= 1;
                    if((iii & 1) != 0)
                        fcs |= 0x80000000;
                    else
                        fcs &= 0x7FFFFFFF;
                    fcs ^= 0xEDB88320;
                } else {
                    fcs>>= 1;
                    if((iii & 1) != 0)
                        fcs |= 0x80000000;
                    else
                        fcs &= 0x7FFFFFFF;
                }
                iii >>= 1;
                //Thread.Sleep(0);
            }
            //Thread.Sleep(0);
        }

        /* Завершить вычисление FCS */
        if((fl & 2) != 0) {
            for(i = 0; i < 32; i++) {
                if((fcs & 1) != 0) {
                    fcs >>= 1;
                    fcs &= 0x7FFFFFFF;
                    fcs ^= 0xEDB88320;
                } else {
                    fcs >>= 1;
                    fcs &= 0x7FFFFFFF;
                }
                //Thread.Sleep(0);
            }
            fcs ^= -1;
        }

        /* Записать результат */
        crc32 = fcs;
    }
    #endregion
}