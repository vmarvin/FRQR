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
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Xml;

namespace nprch
{
    /// <summary>Конфигурация программы</summary>
    public class Configuration
    {
        #region Значения по умолчанию
        private string _path = ""; // путь к коллекции выгрузок
        private string _pattern = "_row_:$speed$;$power$;$plan$;_quality_;"; // шаблон строки выгрузки
        private int _precision = 2; // точность, число знаков после запятой при печати
        private int _deph = 1; // размер выгрузки в часах
        private int _maximum_latency = 10; // максимально допустимая "дыра" в шумоподобном сигнале для присвоения плохого качества
        private int _debug_level_console = 0; // степень подробности отладочной печати в консоль
        private int _debug_level_file = 0; // степень подробности отладочной печати в файл
        private int _debug_level_eventlog = 0; // степень подробности отладочной печати в журнал Windows
        private ConcurrentDictionary<string, Block> _blocks = new ConcurrentDictionary<string, Block>(); // задачи сбора данных по энергоблокам
        private ConcurrentDictionary<ushort, string> _abstract_parameter_names = new ConcurrentDictionary<ushort, string>(); // имена параметров, общие для всех источников данных
        #endregion

        #region Публичный доступ к значениям
        internal string path { get { return this._path; } }
        internal string pattern { get { return this._pattern; } }
        internal int precision { get { return this._precision; } }
        internal int deph { get { return this._deph; } }
        internal int maximum_latency { get { return this._maximum_latency; } }
        internal int debug_level_console { get { return this._debug_level_console; } }
        internal int debug_level_file { get { return this._debug_level_file; } }
        internal int debug_level_eventlog { get { return this._debug_level_eventlog; } }
        internal ConcurrentDictionary<string, Block> blocks { get { return this._blocks; } }
        internal ConcurrentDictionary<ushort, string> abstract_parameter_names { get { return this._abstract_parameter_names; } }
        #endregion

        /// <summary>Загрузка конфигурации из файла</summary>
        public void Load(string file)
        {
            XmlDocument xml = new XmlDocument(); // создаёт заготовку XML-документа

            try
            {
                xml.Load(String.Format(@"{0}\{1}", Program.base_path, file)); // загрузка файла конфигурации в память

                #region Загрузка настроек
                foreach (XmlNode __node in xml.SelectNodes("/settings/options/*"))
                {
                    try
                    {
                        if (__node.Name == "path") _path = __node.InnerText;
                        if (__node.Name == "pattern") _pattern = __node.InnerText;
                        if (__node.Name == "precision") _precision = int.Parse(__node.InnerText);
                        if (__node.Name == "deph") _deph = int.Parse(__node.InnerText);
                        if (__node.Name == "maximum_latency") _maximum_latency = int.Parse(__node.InnerText);
                        if (__node.Name == "debug_level_console") _debug_level_console = int.Parse(__node.InnerText);
                        if (__node.Name == "debug_level_file") _debug_level_file = int.Parse(__node.InnerText);
                        if (__node.Name == "debug_level_eventlog") _debug_level_eventlog = int.Parse(__node.InnerText);
                    }
                    catch (Exception ex) { }
                }

                _parse_pattern(_pattern);
                #endregion

                #region Загрузка конфигурации энергоблоков
                foreach (XmlNode block in xml.SelectNodes("/settings/blocks/*"))
                {
                    if (block.Attributes.GetNamedItem("prefix") != null)
                    {
                        _blocks[block.Name] = new Block();
                        _blocks[block.Name].prefix = block.Attributes.GetNamedItem("prefix").Value;
                    }
                    else
                        break;

                    #region Загрузка конфигурации источников
                    foreach (XmlNode node in xml.SelectNodes("/settings/blocks/" + block.Name + "/sources/*"))
                    {
                        try
                        {
                            SourceConfiguration source = new SourceConfiguration();

                            source.Name = node.Name;

                            if (node.Attributes.GetNamedItem("type") != null)
                            {
                                if (node.Attributes.GetNamedItem("type").Value.ToLower() == "kvint") source.SourceType = SourceType.Kvint;
                                if (node.Attributes.GetNamedItem("type").Value.ToLower() == "odbc") source.SourceType = SourceType.Odbc;
                                if (node.Attributes.GetNamedItem("type").Value.ToLower() == "opc_ua") source.SourceType = SourceType.OpcUa;
                            }

                            #region Архивные станции Квинта
                            if (source.SourceType == SourceType.Kvint)
                            {
                                foreach (XmlNode node_kvint in xml.SelectNodes("/settings/blocks/" + block.Name + "/sources/" + source.Name + "/*"))
                                {
                                    if (node_kvint.Name == "station") source.Stations.Add(node_kvint.InnerText);
                                    if (node_kvint.Name == "parameters")
                                    {
                                        foreach (XmlNode node_kvint_parameters in xml.SelectNodes("/settings/blocks/" + block.Name + "/sources/" + source.Name + "/parameters/*"))
                                        {
                                            _get_parameter_properties(source, node_kvint_parameters);
                                        }
                                    }
                                }
                            }
                            #endregion

                            #region Источники данных ODBC
                            if (source.SourceType == SourceType.Odbc)
                            {
                                foreach (XmlNode node_odbc in xml.SelectNodes("/settings/blocks/" + block.Name + "/sources/" + source.Name + "/*"))
                                {
                                    if (node_odbc.Name == "station") source.Stations.Add(node_odbc.InnerText);
                                    if (node_odbc.Name == "parameters")
                                    {
                                        foreach (XmlNode node_kvint_parameter in xml.SelectNodes("/settings/blocks/" + block.Name + "/sources/" + source.Name + "/parameters/*"))
                                        {
                                            _get_parameter_properties(source, node_kvint_parameter);
                                        }
                                    }
                                }
                            }
                            #endregion

                            #region Серверы архивных данных стандарта OPC UA Historical Access (не поддерживается)
                            #endregion

                            _blocks[block.Name].sources.Add(source); // добавление конфигурации источника в общий список
                        }
                        catch (Exception ex) { }
                    }
                    #endregion
                }
                #endregion
            }
            catch (Exception ex) { Console.WriteLine("ERROR: ошибка загрузки конфигурации: {0}", ex.Message); }
        }

        /// <summary>Извлекает свойства параметра</summary>
        /// <param name="source">Конфигурация источника данных</param>
        /// <param name="node_kvint_parameter">Узел дерева XML файла конфигурации, описывающий одиночный параметр</param>
        private void _get_parameter_properties(SourceConfiguration source, XmlNode node_kvint_parameter)
        {
            // чтобы получить номер параметра по его абстрактному имени, выполняем поиск ключа по значению перебором
            foreach (KeyValuePair<ushort, string> name in _abstract_parameter_names)
                if (name.Value == node_kvint_parameter.Name)
                {
                    source.Parameters[name.Key] = new ParameterConfiguration();

                    // по умолчанию относим сигнал к шумоподобным, если его линейность явно не указана
                    if (node_kvint_parameter.Attributes.GetNamedItem("type") != null)
                        if (node_kvint_parameter.Attributes.GetNamedItem("type").Value == "linear")
                            source.Parameters[name.Key].Type = ParameterType.Linear;

                    // записываем локальное название параметра в источнике данных, кроме источника типа odbc, где для доступа к истории значений используется коллекция sql-запросов
                    if (source.SourceType == SourceType.Odbc)
                    {
                        // для источника данных odbc в качестве имени заносим абстрактное имя параметра
                        source.Parameters[name.Key].Name = name.Value;

                        // заполняем коллекцию sql запросов для получения значений параметров из базы данных
                        foreach (XmlNode query in node_kvint_parameter.SelectNodes("*"))
                        {
                            // при использовании свойства InnerText происходит конвертация экранированных значений xml вида "&lt;" в текстовое представление вида "<"
                            if (query.Name == "query") source.Parameters[name.Key].sql_queries.Add(query.InnerText);
                        }
                    }
                    else
                    {
                        // записываем локальное название параметра в источнике данных, используемое для доступа к истории параметра
                        source.Parameters[name.Key].Name = node_kvint_parameter.InnerText;
                    }
                }
        }

        /// <summary>Извлекает из паттерна набор параметров</summary>
        /// <param name="pattern">Описание структуры строки выходных данных</param>
        /// <returns>Набор параметров</returns>
        private void _parse_pattern(string pattern)
        {
            ushort cnt = 0;
            Collection<string> __parameters = new Collection<string>(); // внутренняя коллекция для отброса повторов

            foreach (string __str in Regex.Split(pattern, @"[;:]+"))
            {
                if (Regex.IsMatch(__str, @"^[$][A-z]+[$]$"))
                {
                    if (!__parameters.Contains(__str.Trim('$')))
                    {
                        __parameters.Add(__str.Trim('$'));
                        _abstract_parameter_names[++cnt] = __str.Trim('$');
                    }
                }
            }
        }
    }

    /// <summary>Целевая конфигурация для энергоблока</summary>
    public class Block
    {
        internal string prefix = "00";
        internal Collection<SourceConfiguration> sources = new Collection<SourceConfiguration>();
    }

    /// <summary>Конфигурация источника данных</summary>
    public class SourceConfiguration
    {
        internal string Name = ""; // название источника
        internal SourceType SourceType = SourceType.Kvint; // тип источника по умолчанию
        internal Collection<string> Stations = new Collection<string>(); // адреса, строки подключения, либо GUID источников данных
        internal ConcurrentDictionary<ushort, ParameterConfiguration> Parameters = new ConcurrentDictionary<ushort, ParameterConfiguration>(); // конечные параметры в целевом источнике данных
    }

    /// <summary>Конфигурация параметра</summary>
    public class ParameterConfiguration
    {
        public string Name = ""; // абстрактное имя параметра
        public ParameterType Type = ParameterType.Noisy; // тип сигнала: шумоподобный / линеаризированный
        public Collection<string> sql_queries = new Collection<string>(); // sql запросы для выборки данных из источника odbc
    }

    /// <summary>Типы параметров: линейные/шумоподобные</summary>
    public enum ParameterType
    {
        Linear,
        Noisy
    }

    /// <summary>Известные типы источников данных</summary>
    public enum SourceType
    {
        Kvint,
        OpcUa,
        Odbc
    }
}
