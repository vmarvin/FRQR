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
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security;
using System.Text;
using System.Threading.Tasks;

namespace nprch
{
    /// <summary>Класс ведения журнала</summary>
    static class Logger
    {
        #region Закрытые структуры данных
        private static int _debug_level_console = 1; // уровень отладочной печати в консоль по умолчанию
        private static int _debug_level_file = 0; // уровень отладочной печати в файл по умолчанию
        private static int _debug_level_eventlog = 0; // уровень отладочной печати в журнал windows по умолчанию
        private static BlockingCollection<string> _message_queue; // буфер для отладочных сообщений
        private static string _filename = Program.base_path + "\\" + Assembly.GetEntryAssembly().GetName().Name + ".log"; // имя файла отладочной печати
        private static Task _task; // задача для асинхронного неблокирующего выполнения операций отладочной печати в файл журнала
        #endregion

        /// <summary>Начинает запись в файл (при наличии доступа к файлу журнала и ненулевом уровне отладочной печати в файл)</summary>
        public static void Open(Configuration config)
        {
            // настройка уровней подробности отладочной печати
            _debug_level_console = config.debug_level_console;
            _debug_level_file = config.debug_level_file;
            _debug_level_eventlog = config.debug_level_eventlog;

            _message_queue = new BlockingCollection<string>();

            try
            {
                if (_debug_level_file > 0)
                {
                    _task = Task.Factory.StartNew(() =>
                    {
                        using (StreamWriter streamWriter = new StreamWriter(_filename, true, Encoding.UTF8))
                        {
                            streamWriter.AutoFlush = true;

                            foreach (var s in _message_queue.GetConsumingEnumerable())
                                streamWriter.WriteLine(s);
                        }
                    },
                    TaskCreationOptions.LongRunning);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("{0} ERROR: не удалось начать запись в файл журнала, ошибка: {1}", DateTime.Now, ex.Message);
            }
        }

        /// <summary>Размещает новые сообщения в очереди на запись</summary>
        public static void WriteLine(string message)
        {
            _message_queue.Add(message);
        }

        /// <summary>Принудительная запись очереди сообщений</summary>
        public static void Flush()
        {
            try
            {
                _message_queue.CompleteAdding();
                _task.Wait();
            }
            catch (Exception ex) { }
        }

        /// <summary>Отладочная печать</summary>
        public static void Log(ushort debug_level, string message)
        {
            if (_debug_level_file >= debug_level) Logger.WriteLine(String.Format("{0} {1}", DateTime.Now, message));// печать в файл журнала на диске
            if (_debug_level_console >= debug_level) Console.Error.WriteLine("{0} {1}", DateTime.Now, message); // печать в канал stderr консоли
            if (_debug_level_eventlog >= debug_level)
            {
                // печать в журнал приложений Windows
                using (EventLog eventLog = new EventLog("Application"))
                {
                    eventLog.Source = WindowsLogCreateEventSource(Assembly.GetEntryAssembly().GetName().Name);
                    eventLog.WriteEntry(String.Format(message), EventLogEntryType.Information, debug_level, 0);
                }
            }
        }

        /// <summary>Проверяет наличие или регистрирует источник событий в журнале приложений Windows (при наличии административных привилегий)</summary>
        public static string WindowsLogCreateEventSource(string currentAppName)
        {
            string eventSource = currentAppName;
            bool sourceExists;
            try
            {
                // если источник событий не зарегистрирован и нет административных привилегий, генерируется исключение безопасности
                sourceExists = EventLog.SourceExists(eventSource);
                if (!sourceExists)
                {   // регистрация источника
                    EventLog.CreateEventSource(eventSource, "Application");
                }
            }
            catch (SecurityException)
            {
                // при отсутствии регистрации и недостаточности полномочий используется системное имя источника по умолчанию
                eventSource = "Application";
            }

            return eventSource;
        }
    }
}
