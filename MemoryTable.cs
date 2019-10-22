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
using System.Data;

namespace nprch
{
    internal class MemoryTable
    {
        internal DataTable memory_table = new DataTable(); // буфер для архивных данных

        /// <summary>Конструктор</summary>
        internal MemoryTable()
        {
            DataColumn column;

            // определение свойств колонки
            column = new DataColumn();
            column.DataType = typeof(ushort);
            column.ColumnName = "index";
            column.AutoIncrement = false;
            column.Caption = column.ColumnName;
            column.ReadOnly = true;
            column.Unique = false;
            column.AllowDBNull = false;
            memory_table.Columns.Add(column); // добавление колонки в таблицу

            column = new DataColumn();
            column.DataType = typeof(DateTime);
            column.ColumnName = "timestamp";
            column.AutoIncrement = false;
            column.Caption = column.ColumnName;
            column.ReadOnly = false;
            column.Unique = false;
            column.AllowDBNull = false;
            memory_table.Columns.Add(column);

            column = new DataColumn();
            column.DataType = typeof(Double);
            column.ColumnName = "value";
            column.AutoIncrement = false;
            column.Caption = column.ColumnName;
            column.ReadOnly = false;
            column.Unique = false;
            column.AllowDBNull = true;
            memory_table.Columns.Add(column);

            column = new DataColumn();
            column.DataType = typeof(Boolean);
            column.ColumnName = "quality";
            column.AutoIncrement = false;
            column.Caption = column.ColumnName;
            column.ReadOnly = false;
            column.Unique = false;
            column.AllowDBNull = false;
            memory_table.Columns.Add(column);

            // создание первичного ключа
            DataColumn[] PrimaryKeyColumns = new DataColumn[2];
            PrimaryKeyColumns[0] = memory_table.Columns["index"];
            PrimaryKeyColumns[1] = memory_table.Columns["timestamp"];
            memory_table.PrimaryKey = PrimaryKeyColumns;
        }

        /// <summary>Записать значение в буфер</summary>
        /// <param name="parameter_index">Номер параметра</param>
        /// <param name="timestamp">Метка времени</param>
        /// <param name="value">Значение в формате double</param>
        /// <param name="quality">Признак качества</param>
        internal void write_value(ushort parameter_index, DateTime timestamp, double value, bool quality)
        {
            Logger.Log(6, String.Format("DEBUG6: параметр {0} = {1:0.00}, с меткой времени {2} и качеством {3} заносится в таблицу в памяти", parameter_index, value, timestamp, quality));

            DataRow row = memory_table.NewRow();
            row["index"] = parameter_index;
            row["timestamp"] = timestamp;
            row["value"] = value;
            row["quality"] = quality;
            object[] index = { row["index"], row["timestamp"] };

            try
            {
                if (!memory_table.Rows.Contains(index)) memory_table.Rows.Add(row);
            }
            catch (Exception ex)
            {
                Logger.Log(0, String.Format("ERROR: ошибка при записи данных в таблицу в памяти {0}", ex.Message));
            }
        }

        /// <summary>Прочитать значение</summary>
        /// <param name="parameter_index">Номер параметра</param>
        /// <param name="request_timestamp">Метка времени</param>
        /// <param name="return_value">Ближайшее, по прошедшему времени, значение параметра в формате double</param>
        /// <returns>true - если значение получено, false - если нет данных</returns>
        internal bool read_sample(ushort parameter_index, DateTime request_timestamp, out DateTime return_timestamp, out double return_value, out bool return_quality)
        {
            return_timestamp = DateTime.MinValue;
            return_value = Double.NaN;
            return_quality = false;
            
            DataRow[] found_rows = memory_table.Select(String.Format("index = '{0}' AND timestamp < '{1}'", parameter_index, request_timestamp.AddSeconds(1)), "timestamp DESC");

            if (found_rows.Length > 0)
            {
                return_timestamp = found_rows[0].Field<DateTime>("timestamp");
                return_value = found_rows[0].Field<double>("value");
                return_quality = found_rows[0].Field<bool>("quality");
                return true;
            }

            return false;
        }
    }
}
