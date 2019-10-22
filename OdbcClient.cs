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
using System.Collections.ObjectModel;
using System.Data.Odbc;
using System.Text.RegularExpressions;

namespace nprch
{
    class OdbcClient
    {
        #region Закрытые структуры данных
        private bool _source_active = false; // текущий статус канала связи с сервером БД
        private bool _prev_state = false; // предыдущий статус канала связи с сервером БД
        private string _connection_string; // строка подключения к серверу БД
        private OdbcConnection _odbc_connection; // подключение к серверу БД
        #endregion

        /// Конструктор
        /// <param name="connection_string">Строка подключения к базе данных в формате ODBC</param>
        public OdbcClient(string connection_string)
        {
            _connection_string = connection_string;
            _odbc_connection = new OdbcConnection(connection_string);
            try
            {
                _odbc_connection.Open();
            }
            catch (Exception ex)
            {
                Logger.Log(0, String.Format("  ERROR: {0}", ex.Message));
            }
        }

        /// <summary>Монитор доступа к источнику данных, инициирует изменение статуса публичных переменных и обновление списка параметров</summary>
        internal void monitor()
        {
            // проверяем доступность базы данных
            get_status();

            // определение события подключения к источнику данных
            if (_source_active && !_prev_state)
            {
                // проверка готовности источника данных и фиксация результата
                if (true)
                {
                    _prev_state = _source_active;
                    Logger.Log(3, String.Format("  INFO3: [{0}] соединение с базой данных установлено", _odbc_connection.DataSource));
                }
                else
                {
                    Logger.Log(4, String.Format("  INFO4: [{0}] база данных не готова", _odbc_connection.DataSource));
                }
            }

            if (!_source_active && _prev_state)
            {
                Logger.Log(3, String.Format("  INFO3: [{0}] соединение с базой данных прервано", _odbc_connection.DataSource));

                _prev_state = _source_active;
            }
        }

        /// <summary>Проверка доступности источника данных</summary>
        private void get_status()
        {
            _source_active = _odbc_connection.State == System.Data.ConnectionState.Open ? true : false;
        }

        /// <summary>Получает часовой набор значений указанного параметра за указанный период</summary>
        /// <param name="start_time">Начало интервала</param>
        /// <param name="parameter_index">Индекс параметра</param>
        /// <param name="sql_queries">Название параметра (не используется в ODBC)</param>
        /// <param name="table">Ссылка на таблицу в памяти для приёма истории значений параметра</param>
        /// <returns>Запрос успешен: параметр существует и получены данные с хорошим качеством</returns>
        internal bool get_hour_interval(DateTime start_time, ushort index, Collection<string> sql_queries, ref MemoryTable table)
        {
            // счётчик принятых успешно данных
            uint count = 0;

            // обработка каждого, из ассоциированных с параметром, запросов
            foreach (var _query in sql_queries)
            {
                // выполняем вставку начала обрабатываемого часа в sql запрос
                string _sql_query = Regex.Replace(_query, "_unix_basetime_", (start_time - new DateTime(1970, 1, 1)).TotalSeconds.ToString());

                // проверка доступности источника данных
                if (_source_active)
                {
                    // выполнение запроса
                    using (OdbcDataReader _reader = new OdbcCommand(_sql_query, _odbc_connection).ExecuteReader())
                    {
                        // циклическое чтение данных
                        while (_reader.Read())
                        {
                            // подсчёт успешно принятых строк данных
                            count++;

                            // запись результатов в таблицу в памяти
                            table.write_value(index, new DateTime(1970, 1, 1).AddSeconds(_reader.GetDouble(0)), _reader.GetDouble(1), true);
                        }
                    }
                }
            }

            return count > 0;
        }
    }
}
