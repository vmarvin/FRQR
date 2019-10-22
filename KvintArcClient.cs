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
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Kvint;

namespace nprch
{
    /// <summary>Клиент архивной станции Квинт-7</summary>
    class KvintArcClient
    {
        #region Закрытые структуры данных
        private string _station; // сетевое имя архивной станции
        private string _service; // название программного канала архивной станции
        private IntPtr _link_id; // дескриптор архивной станции
        private bool _pipe_active = false; // текущий статус канала связи с архивной станцией
        private bool _prev_state = false; // предыдущий статус канала связи с архивной станцией
        private Services.QACallback _receiver; // указатель на делегат для приёма сообщений от архивной станции
        private List<KvintArchive.VT_VOLUME> _stor_volumes = new List<KvintArchive.VT_VOLUME>(); // список томов в 00 цепочке
        private ConcurrentDictionary<int, KvintArchive.VT_CHAIN> _stor_chains = new ConcurrentDictionary<int, KvintArchive.VT_CHAIN>(); // список цепочек в архиве
        private ConcurrentDictionary<ulong, KvintArchive.VT_PARAMETER> _stor_parameters = new ConcurrentDictionary<ulong, KvintArchive.VT_PARAMETER>(); // список параметров в томе
        private ConcurrentDictionary<string, Services.QAParamId> _cached_parameters = new ConcurrentDictionary<string, Services.QAParamId>(); // кэш запрошенных параметров
        #endregion

        #region Публичные свойства класса
        internal bool PipeActive { get { return this._pipe_active; } }
        #endregion

        /// <summary>Создаёт подключение к архивной станции</summary>
        /// <param name="station">Сетевой адрес архивной станции</param>
        /// <param name="service">Имя программного канала архивной станции</param>
        /// <param name="ns">Номер пространства имён, с которым ассоциируется данная архивная станция</param>
        public KvintArcClient(string station, string service)
        {
            this._station = station; // сетевой адрес архивной станции
            this._service = service; // название программного канала службы выдачи данных архивной станции
            this._link_id = Services.ArcClntLogin(station, service); // дескриптор архивной станции
            this._receiver = new Services.QACallback(_get_response); // указатель на метод-приёмник
            monitor(); // первичный запуск монитора
        }

        /// <summary>Деструктор клиента архивной станции, закрывает соединение</summary>
        ~KvintArcClient()
        {
            Services.ArcClntLogout(_link_id);
        }

        /// <summary>Монитор доступа к архивной станции, инициирует изменение статуса публичных переменных и обновление списка параметров</summary>
        internal void monitor()
        {
            get_status(); // проверяем доступность архивной станции
            
            if (_pipe_active && !_prev_state)
            {
                // запрашиваем актуальный список параметров
                if (get_cards())
                {
                    _prev_state = _pipe_active;
                    Logger.Log(3, String.Format("  INFO3: [{0}] соединение с архивной станцией установлено", _station));
                }
                else
                {
                    Logger.Log(4, String.Format("  INFO4: [{0}] архивная станция не готова", _station));
                }
            }

            if (!_pipe_active && _prev_state)
            {
                Logger.Log(3, String.Format("  INFO3: [{0}] соединение с архивной станцией прервано", _station));

                _prev_state = _pipe_active;
            }
        }

        /// <summary>Проверка доступности архивной станции</summary>
        internal void get_status()
        {
            Services.QAQuery query = new Services.QAQuery();
            query.LinkId = _link_id; // идентификатор сессии
            query.Callback = _receiver; // указатель на метод-приёмник
            query.ParamId.Category = 2; // категория параметра: хранилище информации о цепочках томов архива
            query.Flags = Services.QF_NETINFO; // флаг запроса доступности архивной станции по сети (фактически, ping)

            try
            {
                _execute_query(ref query);
            }
            catch (Exception ex)
            {
                Logger.Log(0, String.Format(" ERROR: [{0}] ошибка при получении статуса сервера: {1}", _station, ex.Message));
            }
        }

        /// <summary>Получение списка параметров из архивной станции</summary>
        internal bool get_cards()
        {
            query_chains(); // запрос списка цепочек в архиве

            if (_stor_chains.Count == 0) return false;

            query_volumes(); // запрос списка томов в 00 цепочке архива

            if (_stor_volumes.Count > 0)
            {
                query_parameters(_stor_volumes.Count - 1); // запрос списка параметров в текущем томе
            }
            else
            {
                return false;
            }

            if (_stor_parameters.Count > 0)
            {
                return true;
            }

            return false;
        }

        /// <summary>Запрос списка цепочек</summary>
        protected void query_chains()
        {
            lock (_stor_chains) _stor_chains.Clear(); // очищаем список цепочек
            
            Services.QAQuery query = new Services.QAQuery();
            query.LinkId = _link_id; // идентификатор сессии
            query.Callback = _receiver; // указатель на метод-приёмник
            query.ParamId.Category = 2; // категория параметра: хранилище информации о цепочках томов архива
            query.Flags = Services.QF_STORINFO | Services.QF_MULTIPARAMS; // флаги синхронного многопараметрического запроса информации о структуре хранилища

            Logger.Log(4, String.Format("  INFO4: [{0}] запрос списка цепочек", _station));

            _execute_query(ref query);
        }

        /// <summary>Запрос списка томов</summary>
        protected void query_volumes()
        {
            lock (_stor_volumes) _stor_volumes.Clear(); // очищаем список томов

            if (_stor_chains.Count > 0)
            {
                Services.QAQuery query = new Services.QAQuery();
                query.LinkId = _link_id; // идентификатор сессии
                query.Callback = _receiver; // указатель на метод-приёмник
                query.ParamId.Category = 3; // категория параметра ADC_ArcInfo -> хранилище информации о томах архива
                query.ParamId.CardId = _stor_chains[0].id; // идентификатор цепочки
                query.Flags = Services.QF_STORINFO | Services.QF_MULTIPARAMS; // флаги синхронного многопараметрического запроса информации о структуре хранилища

                Logger.Log(4, String.Format("  INFO4: [{0}] запрос списка томов", _station));
                
                _execute_query(ref query);
            }
        }

        /// <summary>Запрос списка переменных в томе</summary>
        protected void query_parameters(int volume)
        {
            lock (_stor_parameters) _stor_parameters.Clear(); // очищаем список параметров

            if ((_stor_chains.Count > 0) && (_stor_volumes.Count > 0))
            {
                Services.QAQuery query = new Services.QAQuery();
                query.LinkId = _link_id; // идентификатор сессии
                query.Callback = _receiver; // указатель на метод-приёмник
                query.ParamId.Category = 4; // категория параметра: хранилище информации о списке параметров в томе
                query.ParamId.CardId = _stor_chains[0].id; // идентификатор цепочки
                query.Flags = Services.QF_STORINFO | Services.QF_MULTIPARAMS; // флаги синхронного многопараметрического запроса информации о структуре хранилища
                query.BeginTime = _stor_volumes[volume].properties.beginTime; // идентификатор тома в виде целочисленного unixtimestamp времени его начала

                Logger.Log(4, String.Format("  INFO4: [{0}] запрос списка параметров", _station));

                _execute_query(ref query);
            }
        }

        /// <summary>Загрузить параметр в кэш</summary>
        /// <param name="parameter_name">Название параметра</param>
        /// <returns>Описатель параметра</returns>
        internal bool load_parameter(string parameter_name, out Services.QAParamId param_id)
        {
            param_id = new Services.QAParamId();

            if (!_cached_parameters.ContainsKey(parameter_name))
            {
                foreach (KeyValuePair<ulong, KvintArchive.VT_PARAMETER> __parameter in _stor_parameters)
                {
                    if (__parameter.Value.long_name == parameter_name)
                    {
                        Services.QAParamId parameter = new Services.QAParamId();

                        parameter.Category = __parameter.Value.paramInfo.param_type;
                        parameter.CardId = __parameter.Value.paramInfo.card_id;
                        parameter.ParamNo = __parameter.Value.paramInfo.param_no;

                        _cached_parameters[parameter_name] = parameter; // заносим найденый параметр в кеш
                    }
                }
            }

            if (_cached_parameters.ContainsKey(parameter_name))
            {
                param_id = _cached_parameters[parameter_name];
                return true;
            }
            else
            {
                Logger.Log(0, String.Format("WARNING: [{0}] параметр {1} не найден!", _station, parameter_name));
                return false;
            }
        }
        
        /// <summary>Получает часовой набор значений указанного параметра за указанный период</summary>
        /// <param name="start_time">Начало интервала</param>
        /// <param name="parameter_index">Индекс параметра</param>
        /// <param name="station_parameter_name">Локальное название параметра архивной станции</param>
        /// <param name="table">Ссылка на таблицу в памяти для приёма истории значений параметра</param>
        /// <returns>Запрос успешен: параметр существует и получены данные с хорошим качеством</returns>
        internal bool get_hour_interval(DateTime start_time, ushort parameter_index, string station_parameter_name, ref MemoryTable table)
        {
            user_data_reference _user_data_ref = new user_data_reference(); // структура для передачи ссылки на дополнительные параметры через неуправляемый код
            UserData _user_data = new UserData(); // структура для транзита дополнительных параметров через неуправляемый код
            _user_data_ref.data = _user_data; // вставляем в структуру UserData ссылку на класс с дополнительными полями данных
            _user_data_ref.data.parameter_index = parameter_index; // индекс параметра
            _user_data_ref.data.count = 0; // счётчик принятых значений параметров с хорошим качеством
            _user_data_ref.data.table = table; // указательно на таблицу в памяти для приёма сырых значений

            IntPtr pointer = Marshal.AllocHGlobal(Marshal.SizeOf(_user_data_ref)); // распределяем память в куче для копирования туда индекса кэша
            Marshal.StructureToPtr(_user_data_ref, pointer, false); // копируем индекс кэша в кучу
            
            Services.QAQuery query = new Services.QAQuery(); // структура, описывающая запрос к АС
            query.LinkId = _link_id; // дескриптор подключения к архивной станции
            query.Callback = new Services.QACallback(_get_historical_data); // процедура для приёма данных от АС
            query.BeginTime = new Services.QATime(start_time); // начало запрашиваемого интервала
            query.EndTime = new Services.QATime(start_time.AddHours(1)); // конец запрашиваемого интервала
            query.Flags = Services.QF_BEGINOUTSIDE | Services.QF_FILTLAST; // флаги запроса
            query.Accuracy = 1; // шаг времени для просеивания
            query.UserData = pointer; // указатель на буфер в неуправляемой памяти с дополнительными параметрами запроса

            bool parameter_exist = load_parameter(station_parameter_name, out query.ParamId); // проверка наличия параметра на АС и получение его атрибутов

            if (parameter_exist)
            {
                Logger.Log(4, String.Format(" INFO4: [{0}] запрос значений параметра {1}", _station, station_parameter_name));

                _execute_query(ref query);
            }

            Marshal.FreeHGlobal(pointer); // освобождаем выделенную в куче память

            return _user_data_ref.data.count > 0; // признак успешности запроса
        }
        
        /// <summary>Отправляет запрос к АС на выполнение</summary>
        private void _execute_query(ref Services.QAQuery query)
        {
            try
            {
                Services.ArcClntQuery(ref query); // исполняет запрос
            }
            catch (Exception ex)
            {
                Logger.Log(0, String.Format(" ERROR: [{0}] ошибка при выполнении запроса: {1}", _station, ex.Message));
            }
        }

        /// <summary>Приёмник сообщений архивной станции</summary>
        /// <param name="Response">Единичный элемент данных архивной станции</param>
        /// <returns>Признак готовности к приёму следующего сообщения</returns>
        private int _get_response(ref Services.QAResponse Response)
        {
            try
            {
                if (Response.Status == Services.RS_OK)
                {
                    _pipe_active = true; // приём любого ответа со статусом RS_OK означает рабочее состояние канала связи с архивной станцией

                    #region заполнение словаря цепочек
                    if (Response.ParamId.Category == 2)
                    {
                        KvintArchive.VT_CHAIN chain = new KvintArchive.VT_CHAIN();
                        chain.id = Response.ParamId.CardId;
                        chain.desc = Marshal.PtrToStringAnsi(Response.Value + 241);
                        chain.path = Marshal.PtrToStringAnsi(Response.Value + 241 + Marshal.PtrToStringAnsi(Response.Value + 241).Length + 1);
                        _stor_chains[Response.ParamId.ParamNo] = chain;
                    }
                    #endregion

                    #region заполнение словаря томов в основной цепочке
                    if (Response.ParamId.Category == 3)
                    {
                        KvintArchive.VT_VOLUME volume = new KvintArchive.VT_VOLUME();

                        volume.properties = (KvintArchive.VT_VOLINFO)Marshal.PtrToStructure(Response.Value, typeof(KvintArchive.VT_VOLINFO));
                        volume.full_name = Marshal.PtrToStringAnsi(Response.Value + 192);

                        string[] parts = Regex.Split(volume.full_name, @"\\");
                        volume.name = parts[parts.Length - 1].Trim(null);

                        _stor_volumes.Add(volume);
                    }
                    #endregion

                    #region заполнение списка параметров
                    if (Response.ParamId.Category == 4)
                    {
                        KvintArchive.VT_PARAMETER parameter = new KvintArchive.VT_PARAMETER();
                        KvintArchive.VT_PARAMINFO paraminfo = (KvintArchive.VT_PARAMINFO)Marshal.PtrToStructure(Response.Value, typeof(KvintArchive.VT_PARAMINFO));

                        if (paraminfo.param_type == 7)
                        {
                            parameter.paramInfo = paraminfo;
                            parameter.long_name = Marshal.PtrToStringAnsi(Response.Value + 64);

                            string[] parts = Regex.Split(parameter.long_name, @"\.");
                            parameter.card_name = parts[0];
                            parameter.param_name = parts[1];

                            _stor_parameters[(ulong)parameter.paramInfo.card_id + ((ulong)parameter.paramInfo.param_no << 32)] = parameter;
                        }
                    }
                    #endregion
                }

                if (Response.Status == Services.RS_PIPEERROR) _pipe_active = false;      // ошибка программного канала - связь с АС потеряна
                if (Response.Status == Services.RS_OPENPIPEFAILED) _pipe_active = false; // ошибка открытия программного канала - связь с АС не установлена

                if (_pipe_active == false) return 0; // 0 - прекращает приём данных, завершая текущий запрос
            }
            catch (Exception ex)
            {
                Logger.Log(0, String.Format(" ERROR: [{0}] ошибка при приёме данных: {1}", _station, ex.Message));
            }

            return 1; // 1 - готов продолжать приём данных по текущему запросу
        }

        /// <summary>Приёмник сообщений архивной станции</summary>
        /// <param name="Response">Единичный элемент данных архивной станции</param>
        /// <returns>Признак готовности к приёму следующего сообщения</returns>
        private int _get_historical_data(ref Services.QAResponse Response)
        {
            try
            {
                user_data_reference _user_data_ref = (user_data_reference)Marshal.PtrToStructure(Response.UserData, typeof(user_data_reference)); // копирует локальную копию индекса кэша в управляемую память

                if (Response.Status == Services.RS_OK)
                {
                    _pipe_active = true; // приём любого ответа со статусом RS_OK означает рабочее состояние канала связи с архивной станцией

                    if (Response.ParamId.CardId != 0 && Response.ParamId.ParamNo != 0)
                    {
                        if (Response.ParamId.Category == 7)
                        {
                            if (Response.ValueFormat == (int)Services.QAValueFormat.VF_QR8)
                            {
                                KvintArchive.VT_QR8 vt_f = (KvintArchive.VT_QR8)Marshal.PtrToStructure(Response.Value, typeof(KvintArchive.VT_QR8));
                                _user_data_ref.data.table.write_value(_user_data_ref.data.parameter_index, Response.Time.AsDateTime, vt_f.value, vt_f.quality < 64);
                                if (vt_f.quality < 64) _user_data_ref.data.count++;
                            }

                            if (Response.ValueFormat == (int)Services.QAValueFormat.VF_QUI4)
                            {
                                KvintArchive.VT_QUI4 vt_i = (KvintArchive.VT_QUI4)Marshal.PtrToStructure(Response.Value, typeof(KvintArchive.VT_QUI4));
                                _user_data_ref.data.table.write_value(_user_data_ref.data.parameter_index, Response.Time.AsDateTime, (double)vt_i.value, vt_i.quality < 64);
                                if (vt_i.quality < 64) _user_data_ref.data.count++;
                            }

                            if (Response.ValueFormat == (int)Services.QAValueFormat.VF_QUI2)
                            {
                                KvintArchive.VT_QUI2 vt_i = (KvintArchive.VT_QUI2)Marshal.PtrToStructure(Response.Value, typeof(KvintArchive.VT_QUI2));
                                _user_data_ref.data.table.write_value(_user_data_ref.data.parameter_index, Response.Time.AsDateTime, (double)vt_i.value, vt_i.quality < 64);
                                if (vt_i.quality < 64) _user_data_ref.data.count++;
                            }
                        }
                    }

                    return 1; // продолжать приём данных
                }
                    
                if (Response.Status == Services.RS_PIPEERROR) _pipe_active = false;      // ошибка программного канала - связь с АС потеряна
                if (Response.Status == Services.RS_OPENPIPEFAILED) _pipe_active = false; // ошибка открытия программного канала - связь с АС не установлена

                if (_pipe_active == false) return 0; // 0 - прекращает приём данных, завершая текущий запрос
            }
            catch (Exception ex)
            {
                Logger.Log(0, String.Format(" ERROR: [{0}] ошибка при приёме данных: {1}", _station, ex.Message));
            }

            return 0; // прекратить приём данных
        }
    }

    // класс для передачи дополнительных аргументов методу обратного вызова
    internal class UserData
    {
        public ushort parameter_index;
        public ulong count;
        public MemoryTable table;
    }

    // структура для передачи ссылки на дополнительные аргументы через буфер в неуправляемой памяти
    internal struct user_data_reference
    {
        // ссылка на класс с дополнительными аргументами вызова
        // передача по ссылке делает возможным возврат дополнительных данных из процедуры обратного вызова
        public UserData data;
    }
}
