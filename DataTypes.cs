/* ========================================================================
 * Copyright (c) 2019 <vmarvin@gmail.com>. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * 
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 * 
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using System.Runtime.InteropServices;
using Kvint;

namespace nprch
{
    /// <summary>Структуры данных архивных станций Квинт-7</summary>
    class KvintArchive
    {
        /// <summary>Квинт-7: UInt16 с качеством</summary>
        [StructLayout(LayoutKind.Explicit)]
        public struct VT_QUI2
        {
            [FieldOffset(0)]
            public byte code;       // дополнительный код ошибки или типа качества
            [FieldOffset(1)]
            public byte quality;    // код качества: 0 - хорошее; 64 - сомнительное; 128 - плохое;
            [FieldOffset(2)]
            public UInt16 value;     // значение
        }

        /// <summary>Квинт-7: UInt32 с качеством</summary>
        [StructLayout(LayoutKind.Explicit)]
        public struct VT_QUI4
        {
            [FieldOffset(0)]
            public byte code;       // дополнительный код ошибки или типа качества
            [FieldOffset(1)]
            public byte quality;    // код качества: 0 - хорошее; 64 - сомнительное; 128 - плохое;
            [FieldOffset(2)]
            public UInt32 value;     // значение
        }

        /// <summary>Квинт-7: Double с качеством</summary>
        [StructLayout(LayoutKind.Explicit)]
        public struct VT_QR8
        {
            [FieldOffset(0)]
            public byte code;       // дополнительный код ошибки или типа качества
            [FieldOffset(1)]
            public byte quality;    // код качества: 0 - хорошее; 64 - сомнительное; 128 - плохое;
            [FieldOffset(2)]
            public double value;    // значение
        }

        /// <summary>Архивная станция: информация о подключенном томе</summary>
        [StructLayout(LayoutKind.Explicit)]
        public struct VT_VOLINFO
        {
            // Структура информации о томе архивных данных
            // Примечание: размеры файлов возвращаются, как правило, на 3 байта меньше, чем в файловой системе
            [FieldOffset(0)]
            public byte writing; // открыт на запись
            [FieldOffset(1)]
            public byte hasReserve; // открыт файл резерва
            [FieldOffset(2)]
            public byte newTable; // сопровождается обновлённой таблицей параметров *.art
            [FieldOffset(3)]
            public byte hasPrev; // есть файл предшествующих данных
            [FieldOffset(4)]
            public byte memoryMapped; // отображён в оперативную память
            [FieldOffset(8)]
            public int volumeNumber; // номер тома в цепочке
            [FieldOffset(12)]
            public int reserveFlushPeriod; // период сброса буфера резерва
            [FieldOffset(16)]
            public int reserveBufferSize; // размер буфера резерва
            [FieldOffset(20)]
            public int minBlockSize; // минимальный размер блока данных
            [FieldOffset(24)]
            public int maxBlockSize; // максимальный размер блока данных
            [FieldOffset(28)]
            public UInt64 unknown28; // неизвестный параметр
            [FieldOffset(36)]
            public int paramsNumber; // количество параметров
            [FieldOffset(40)]
            public int tableSize; // размер файла таблицы *.art
            [FieldOffset(44)]
            public Services.QATime beginTime; // начальное время
            [FieldOffset(52)]
            public Services.QATime timeMultiplier; // временной множитель (WTF?!)
            [FieldOffset(68)]
            public Services.QATime tableTime; // время таблицы
            [FieldOffset(76)]
            public int reformCode; // код переформирования
            [FieldOffset(136)]
            public int reserveBufferFill; // заполнение буфера резерва
            [FieldOffset(140)]
            public int reserveFill; // заполнение файла резерва
            [FieldOffset(144)]
            public int reserveSize; // размер файла резерва
            [FieldOffset(148)]
            public int fileFill; // заполнение файла данных *.ar1
            [FieldOffset(152)]
            public int refFill; // заполнение файла ссылок *.ar2
            [FieldOffset(156)]
            public int prevSize; // размер файла предыдущих данных *.ar3
            [FieldOffset(160)]
            public int fileSize; // размер файла данных *.ar1
            [FieldOffset(164)]
            public int refSize; // размер файла ссылок *.ar2
            [FieldOffset(168)]
            public Services.QATime endTime; // конечное время
            [FieldOffset(176)]
            public Services.QATime lastPrevTime; // последнее предшествующее время из файла *.ar3
        }

        /// <summary>Архивная станция Квинт: информация о параметре в подключенном томе</summary>
        [StructLayout(LayoutKind.Explicit)]
        public struct VT_PARAMINFO
        {
            // Структура информации о параметре в томе
            [FieldOffset(0)]
            public short param_type; // тип параметра (1 - старый, 7 - новый, остальные - служебные)
            [FieldOffset(2)]
            public int card_id; // группа (соответствует номеру марки для основных типов)
            [FieldOffset(6)]
            public short param_no; // номер параметра у марки
            [FieldOffset(8)]
            public int param_index; // индекс параметра в таблице *.art
            [FieldOffset(12)]
            public int param_size; // размер параметра
            [FieldOffset(16)]
            public int param_format; // тип параметра согласно Services.QAValueFormat
            [FieldOffset(20)]
            public int unknown20; // неизвестный параметр
            [FieldOffset(24)]
            public int size; // занимаемое место в архиве
            [FieldOffset(28)]
            public int records; // количество записей данного параметра в томе
            [FieldOffset(32)]
            public Services.QATime beginTime; // начальное время
            [FieldOffset(40)]
            public Services.QATime endTime; // конечное время
            [FieldOffset(56)]
            public Services.QATime prevTime; // последнее предшествующее время из файла *.ar3
        }

        /// <summary>Архивная станция: информация о цепочке томов</summary>
        public struct VT_CHAIN
        {
            public int id;
            public string desc;
            public string path;
        }

        /// <summary>Информация о томе</summary>
        public struct VT_VOLUME
        {
            public VT_VOLINFO properties;
            public string name;
            public string full_name;
        }

        /// <summary>Информация о параметре</summary>
        public struct VT_PARAMETER
        {
            public VT_PARAMINFO paramInfo;
            public string long_name;
            public string card_name;
            public string param_name;
        }

        /// <summary>Квинт-7: категории качества сигнала</summary>
        public enum Quality
        {
            Good = 0,
            Uncertain = 64,
            Bad = 128,
            NoData = 144
        }
    }
}