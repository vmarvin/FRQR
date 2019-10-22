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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;

namespace nprch
{
    /// <summary>Коллекция вспомогательных функций</summary>
    class Utils
    {
        /// <summary>Конвертирует длинный идентификатор в bytestream</summary>
        public static byte[] get_octet_id(ulong id)
        {
            return BitConverter.GetBytes(id).Take(5).ToArray();
        }

        /// <summary>Вычисляет длинный идентификатор параметра</summary>
        public static ulong get_long_id(int card_id, short param_no)
        {
            return (ulong)card_id + ((ulong)param_no << 32); // вычисляем длинный идентификатор параметра
        }

        /// <summary>Конвертирует bytestream в длинный идентификатор параметра</summary>
        public static ulong parse_long_id(byte[] octets)
        {
            int card_id;
            short param_no;
            return parse_octet_id(octets, out card_id, out param_no);
        }

        /// <summary>Конвертирует bytestream в длинный идентификатор параметра</summary>
        public static ulong parse_octet_id(byte[] octets, out int card_id, out short param_no)
        {
            card_id = BitConverter.ToInt32(octets, 0);
            param_no = octets[4];
            return get_long_id(card_id, param_no);
        }

        /// <summary>Конвертирует bytestream в длинный идентификатор параметра</summary>
        public static ulong parse_octet_id(byte[] octets)
        {
            int card_id;
            short param_no;

            card_id = BitConverter.ToInt32(octets, 0);
            param_no = octets[4];
            return get_long_id(card_id, param_no);
        }

        /// <summary>Конвертирует длинный идентификатор параметра в марку и параметр</summary>
        public static void parse_long_id(ulong id, out int card_id, out short param_no)
        {
            param_no = (short)(id >> 32);
            card_id = (int)(id - ((ulong)param_no << 32));
        }

        /// <summary>Преобразует перечисление в коллекцию для программной обработки</summary>
        public static Dictionary<int, string> GetEnumList<T>()
        {
            Type enumType = typeof(T);
            if (!enumType.IsEnum)
                throw new Exception("Type parameter should be of enum type");

            return Enum.GetValues(enumType).Cast<int>().ToDictionary(v => v, v => Enum.GetName(enumType, v));
        }

        /// <summary>Ожидание подтверждения пользователем</summary>
        public static void UserWait()
        {
            Console.Error.Write("<press any key>");
            Console.ReadKey();
        }

        /// <summary>Извлекает из паттерна набор параметров</summary>
        /// <param name="pattern">Описание структуры строки выходных данных</param>
        /// <returns>Набор параметров</returns>
        public static Collection<string> parse_pattern(string pattern)
        {
            Collection<string> _parameters = new Collection<string>();

            foreach (string __str in Regex.Split(pattern, @"[;:]+"))
            {
                if (Regex.IsMatch(__str, @"^[$][A-z]+[$]$"))
                {
                    if (!_parameters.Contains(__str.Trim('$'))) _parameters.Add(__str.Trim('$'));
                }
            }

            return _parameters;
        }
    }
}
