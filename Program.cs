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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Ionic.Zip;

namespace nprch
{
    class Program
    {
        #region Глобальные структуры данных
        internal static string base_path = Application.StartupPath; // путь к каталогу запуска программы, получаемый вызовом GetModuleFileName
        internal static ConcurrentDictionary<string, KvintArcClient> kvint_stations = new ConcurrentDictionary<string, KvintArcClient>(); // архивные станции Квинта
        internal static ConcurrentDictionary<string, OdbcClient> odbc_sources = new ConcurrentDictionary<string, OdbcClient>(); // источники данных ODBC
        internal static NumberFormatInfo nfi = new CultureInfo("en-US", false).NumberFormat; // формат печати чисел с точкой, для формирования sql запросов
        internal static DateTime timeFrom = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day, DateTime.Now.Hour, 0, 0); // начало текущего часа
        #endregion

        static void Main(string[] args)
        {
            // представляемся
            Console.WriteLine("Генератор выгрузки данных об участии энергоблоков в НПРЧ. Версия {0}", Assembly.GetEntryAssembly().GetName().Version);
            Console.WriteLine("Copyright (c) Евгений Юсов 2019. Все права защищены.");
            Console.WriteLine();

            // создание конфигурации
            Configuration config = new Configuration();

            // загрузка конфигурации из файла
            config.Load(Assembly.GetEntryAssembly().GetName().Name + ".xml");

            // инициализация отладочной печати
            Logger.Open(config);

            #region Регистрация источников данных в общих списках по типу
            foreach (var _block in config.blocks)
            {
                // получение конфигурации источников данных
                foreach (SourceConfiguration _source in _block.Value.sources)
                {
                    foreach (string __station in _source.Stations)
                    {
                        switch (_source.SourceType)
                        {
                            case SourceType.Kvint:
                                if (!kvint_stations.ContainsKey(__station)) kvint_stations[__station] = new KvintArcClient(__station, "");
                                break;
                            
                            case SourceType.Odbc:
                                if (!odbc_sources.ContainsKey(__station)) odbc_sources[__station] = new OdbcClient(__station);
                                break;
                            
                            default:
                                break;
                        }
                    }
                }
            }
            #endregion

            #region Асинхронный мониторинг активности источников данных в отдельной задаче
            Task inner = Task.Factory.StartNew(() =>
            {
                // циклический вызов мониторов источников данных с целью отслеживания событий потери и восстановления связи, обновления списков параметров и возобновления подписок
                while (true)
                {
                    foreach (KeyValuePair<string, KvintArcClient> station in kvint_stations) station.Value.monitor();
                    foreach (KeyValuePair<string, OdbcClient> client in odbc_sources) client.Value.monitor();
                    Thread.Sleep(1000);
                }
            }, TaskCreationOptions.AttachedToParent); // задача выполняется, пока жив родительский процесс
            #endregion

            #region Формирование выгрузок архивных данных на заданное число часов в прошлое
            for (int count = config.deph; count >= 1; count--)
            {
                // обработка выполняется для каждого блока по отдельности
                foreach (var _block in config.blocks)
                {
                    DateTime _processing_hour = timeFrom.ToUniversalTime().AddHours(-count);
                    long _result_quality = 0; // результирующее качества текущей задачи
                    string _result_source = ""; // отобранный источник данных
                    MemoryStream _result_stream = new MemoryStream(); // вывод текущей задачи

                    // путь и имя файла выгрузки
                    string _file_path = String.Format(@"{0}\{1}\{2:0000}\{3:00}\{4:00}", config.path, _block.Value.prefix, _processing_hour.Year, _processing_hour.Month, _processing_hour.Day);
                    string _file_name = String.Format("{0}{1:0000}{2:00}{3:00}{4:00}.txt", _block.Value.prefix, _processing_hour.Year, _processing_hour.Month, _processing_hour.Day, _processing_hour.Hour);

                    // при обращении к внешним ресурсам применяется структурированная обработка исключений SEH для перехвата возможных ошибок
                    try
                    {
                        Directory.CreateDirectory(_file_path); // создаёт цепочку каталогов по указанному пути

                        // проверка наличия ранее созданой часовой выгрузки и её создание при отсутствии
                        if (!File.Exists(String.Format(@"{0}\{1}.zip", _file_path, _file_name)))
                        {
                            // запись информации о выгрузке в протокол
                            Logger.Log(1, String.Format(@"  INFO1: [{0}] для интервала {1} UTC формируется архив {2}\{3}.zip", _block.Key, _processing_hour, _file_path, _file_name));

                            // каждый источник данных обрабатывается отдельно для возможности отбора набора данных с наименьшим числом ошибок
                            foreach (SourceConfiguration _source in _block.Value.sources)
                            {
                                long _quality = 0; // качество данных текущего источника
                                MemoryTable _table; // таблица в памяти для приёма сырых данных
                                MemoryStream _buffer; // буфер в памяти для накопления обработанных данных

                                // считывание сырых значений параметров на часовом интервале в буфер в памяти
                                bool _readed = ReadInterval(_source, _processing_hour, out _table);

                                if (_readed)
                                {
                                    // преобразование сырых значений в набор строк по шаблону
                                    _quality = ProcessInterval(config, _source, _processing_hour, ref _table, out _buffer);

                                    // запись предупреждения о наличии данных плохого качества в текущем источнике данных в протокол
                                    if (_quality < 3600) Logger.Log(1, String.Format("WARNING: [{0}] интервал {1} UTC источника {2} содержит {3} строк данных с плохим качеством", _block.Key, _processing_hour, _source.Name, 3600 - _quality));

                                    // отбор наборов данных с наилучшим качеством
                                    if (_quality > _result_quality)
                                    {
                                        _result_source = _source.Name;
                                        _result_quality = _quality;
                                        _result_stream = _buffer;
                                    }
                                }
                            }
                        }

                        // архив с выгрузкой создаётся только в случае успешного запроса данных у источников
                        if (_result_quality > 0)
                        {
                            // запись информации о выбраном источнике данных в протокол
                            Logger.Log(2, String.Format(@"  INFO2: [{0}] для интервала {1} UTC выбран источник {2}", _block.Key, _processing_hour, _result_source));

                            ZipFile zip_file = new ZipFile(); // контейнер для архивного файла
                            zip_file.CompressionLevel = Ionic.Zlib.CompressionLevel.BestCompression; // настройка уровня сжатия
                            ZipEntry entry = zip_file.AddEntry(String.Format("{0}", _file_name), _result_stream); // запись данных в архив
                            zip_file.Save(String.Format(@"{0}\{1}.zip", _file_path, _file_name)); // закрытие файла архива
                        }
                    }
                    catch (Exception ex) { Logger.Log(0, String.Format(" ERROR: {0}", ex.Message)); break; }
                }
            }
            #endregion
        }

        /// <summary>Получить значения параметров источника данных за указанный час</summary>
        /// <param name="begin_time">Начало часового интервала</param>
        /// <param name="source">Описатель источника данных</param>
        /// <param name="table">Буфер в памяти для приёма значений параметров</param>
        private static bool ReadInterval(SourceConfiguration source, DateTime begin_time, out MemoryTable table)
        {
            // диагностическая переменная для определения успешных запросов к группам источников данных
            bool result = false;

            // инициализация таблицы в памяти для приёма сырых данных
            table = new MemoryTable();

            foreach (string _station in source.Stations)
            {
                // диагностическая переменная для определения успешных запросов к отдельным источникам данных
                bool _result_per_station = false;

                // чтение истории значений каждого параметра по отдельности
                foreach (KeyValuePair<ushort, ParameterConfiguration> __parameter in source.Parameters)
                {
                    // обращение к источникам данных по типу
                    switch (source.SourceType)
                    {
                        case SourceType.Kvint:
                            _result_per_station = kvint_stations[_station].get_hour_interval(begin_time, __parameter.Key, __parameter.Value.Name, ref table);
                            break;

                        case SourceType.Odbc:
                            _result_per_station = odbc_sources[_station].get_hour_interval(begin_time, __parameter.Key, __parameter.Value.sql_queries, ref table);
                            break;

                        default:
                            break;
                    }

                    if (!_result_per_station) break; // при отсутствии данных по одному из параметров, дальнейший запрос источника прекращается
                }

                result = result || _result_per_station; // чтение интервала успешно, если успешен запрос хотя бы одного источника
            }

            return result;
        }

        /// <summary>Преобразование сырых данных в набор строк по шаблону</summary>
        /// <param name="config">Конфигурация</param>
        /// <param name="begin_time">Начало часового интервала</param>
        /// <param name="table">Таблица в памяти для принятых данных</param>
        /// <param name="buffer">Буфер в памяти для обработанных данных</param>
        private static long ProcessInterval(Configuration config, SourceConfiguration source, DateTime begin_time, ref MemoryTable table, out MemoryStream buffer)
        {
            long quality = 0; // результирующее качество выборки по числу строк с хорошим качеством
            buffer = new MemoryStream(); // создаёт поток в памяти для записи выборки из текущего источника
            StreamWriter writer = new StreamWriter(buffer, Encoding.UTF8); // создаёт писатель текстовых данных в бинарный поток

            for (uint row = 0; row < 3600; row++)
            {
                int row_quality = 0; // результирующее качество строки данных
                string result = config.pattern;

                // запрос строки: названия параметров заменяются ближайшими по времени значениями
                foreach (string _str in Regex.Split(config.pattern, @"[;:]+"))
                {
                    result = result.Replace("_row_", row.ToString()); // вставка номера строки (секунды)

                    if (Regex.IsMatch(_str, @"^[$][A-z]+[$]$"))
                    {
                        // чтобы получить номер параметра по его абстрактному имени, выполняем поиск ключа по значению перебором
                        foreach (KeyValuePair<ushort, string> __abstract_name in config.abstract_parameter_names)
                        {
                            if (__abstract_name.Value == _str.Trim('$'))
                            {
                                // объявления переменных для запроса значения параметра
                                bool column_quality;
                                double column_value;
                                DateTime column_timestamp;

                                // запрос значения параметра, ближайшего по запрашиваемому времени
                                if (table.read_sample(__abstract_name.Key, begin_time.AddSeconds(row), out column_timestamp, out column_value, out column_quality))
                                {
                                    // для шумоподобных параметров анализируется отставание метки времени последнего полученного значения от времени запроса
                                    // если отставание превышает maximum_latency секунд, строке присваивается плохое качество
                                    if (source.Parameters[__abstract_name.Key].Type == ParameterType.Noisy &&
                                        (begin_time.AddSeconds(row) - column_timestamp).CompareTo(new TimeSpan(0, 0, config.maximum_latency)) > 0) column_quality = false;

                                    // замещение названий параметров значениями
                                    result = result.Replace(_str, String.Format(nfi, "{0:F" + config.precision + "}", column_value));

                                    // подсчёт количества значений с хорошим качеством для последующей оценки качества строки
                                    row_quality = row_quality + (column_quality ? 1 : 0);
                                }
                            }
                        }
                    }
                }

                // вставка интегрального качества выборки, вычисляется сравнением количества значений с хорошим качеством, с общим количеством зарегистрированных параметров
                result = result.Replace("_quality_", row_quality == config.abstract_parameter_names.Count ? "1" : "0");
                
                // запись строки с результатом в поток с результатом для текущего источника
                writer.WriteLine("{0}", result);

                // подсчёт строк с хорошим качеством
                if (row_quality > 0) quality++;
            }

            writer.Flush(); // сброс буфера текстового писателя потока
            buffer.Seek(0, SeekOrigin.Begin); // перемещение файлового указателя потока в начальную позицию
            return quality; // возврат интегрального качества выборки
        }
    }
}
