﻿namespace ICSSoft.STORMNET
{
    using System;
    using System.Collections;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.IO;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Reflection;
    using System.Text;
    using System.Text.RegularExpressions;
    using ICSSoft.Services;
    using ICSSoft.STORMNET.Collections;
    using ICSSoft.STORMNET.Exceptions;
    using ICSSoft.STORMNET.Security;
    using Microsoft.Spatial;
    using Unity;

    #region class Information

    /// <summary>
    /// Доступ к метаданным.
    /// </summary>
    public static class Information
    {
        #region Информация о генераторе первичных ключей

        /// <summary>
        /// Получить .Net-тип генератора первичных ключей, указываемого атрибутом KeyGeneratorAttribute.
        /// </summary>
        private static TypeAtrValueCollection cacheGetKeyGeneratorType = new TypeAtrValueCollection();

        /// <summary>
        /// Получить тип генератора ключей.
        /// </summary>
        /// <param name="typeofdataobject">для какого типа.</param>
        /// <returns></returns>
        public static System.Type GetKeyGeneratorType(System.Type typeofdataobject)
        {
            lock (cacheGetKeyGeneratorType)
            {
                Type res = (Type)cacheGetKeyGeneratorType[typeofdataobject];
                if (res != null)
                {
                    return res;
                }
                else
                {
                    object[] genattrs = typeofdataobject.GetCustomAttributes(typeof(KeyGeneratorAttribute), true);
                    res = ((KeyGeneratorAttribute)genattrs[0]).TypeOfGenerator;
                    cacheGetKeyGeneratorType[typeofdataobject] = res;
                    return res;
                }
            }
        }

        #endregion

        #region Доступ к свойствам класса

        /// <summary>
        /// кэш для делегатов получения значения свойств из объектов.
        /// </summary>
        private static ConcurrentDictionary<long, GetHandler> cacheGetPropValueByName = new ConcurrentDictionary<long, GetHandler>();

        /// <summary>
        /// Получить делегат <see cref="GetHandler" /> для быстрого доступа к свойствам.
        /// </summary>
        /// <param name="type">Тип данных.</param>
        /// <param name="propInfo">Метаданные о свойстве.</param>
        /// <returns>Делегат.</returns>
        internal static GetHandler GetGetHandler(Type type, PropertyInfo propInfo)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (propInfo == null)
            {
                throw new ArgumentNullException(nameof(propInfo));
            }

            long key = (type.GetHashCode() * 10000000000) + propInfo.Name.GetHashCode();
            return cacheGetPropValueByName.GetOrAdd(key, k => DynamicMethodCompiler.CreateGetHandler(type, propInfo));
        }

        /// <summary>
        /// Получить делегат <see cref="GetHandler" /> для быстрого доступа к полям.
        /// </summary>
        /// <param name="type">Тип данных.</param>
        /// <param name="fieldInfo">Метаданные о поле.</param>
        /// <returns>Делегат.</returns>
        internal static GetHandler GetGetHandler(Type type, FieldInfo fieldInfo)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (fieldInfo == null)
            {
                throw new ArgumentNullException(nameof(fieldInfo));
            }

            long key = (type.GetHashCode() * 10000000000) + fieldInfo.Name.GetHashCode();
            return cacheGetPropValueByName.GetOrAdd(key, k => DynamicMethodCompiler.CreateGetHandler(type, fieldInfo));
        }

        /// <summary>
        /// Получить значение свойства объекта данных по имени этого свойства.
        /// </summary>
        /// <param name="obj">Объект данных, значение свойства которого извлекается данным методом. </param>
        /// <param name="propName">Имя свойства объекта данных, значение которого извлекается данным методом.</param>
        /// <returns>Значение свойства объекта данных, которое извлечено данным методом.</returns>
        public static object GetPropValueByName(DataObject obj, string propName)
        {
            if (obj == null)
            {
                return null;
            }

            if (propName == null)
            {
                throw new ArgumentNullException(nameof(propName));
            }

            int pointIndex = propName.IndexOf(".", StringComparison.Ordinal);
            if (pointIndex >= 0)
            {
                string masterName = propName.Substring(0, pointIndex);
                propName = propName.Substring(pointIndex + 1);
                DataObject masterObject = (DataObject)GetPropValueByName(obj, masterName);
                if (masterObject == null)
                {
                    return null;
                }

                return GetPropValueByName(masterObject, propName);
            }

            object value = null;
            Type tp = obj.GetType();
            PropertyInfo pi = tp.GetProperty(propName);
            if (pi == null && obj.DynamicProperties.ContainsKey(propName))
            {
                value = obj.DynamicProperties[propName];
            }
            else if (pi != null) // надо проверить что такое свойство есть
            {
                GetHandler getHandler = GetGetHandler(tp, pi);

                try
                {
                    value = getHandler(obj);
                }
                catch (InvalidProgramException)
                {
                    // сюда вываливаются, например, статические свойства (хотя что они делают в этом методе - непонятно)
                    value = pi.GetValue(obj, null);
                }

                if (value != null && pi.PropertyType == typeof(string))
                {
                    if (TrimmedStringStorage(tp, propName))
                    {
                        value = ((string)value).Trim();
                    }
                }
            }

            return value;
        }

        private static TypePropertyAtrValueCollection cacheTrimmedStringStorage = new TypePropertyAtrValueCollection();

        /// <summary>
        /// Обрезать ли строки для данного свойства.
        /// </summary>
        /// <param name="tp">тип.</param>
        /// <param name="propname">свойство.</param>
        /// <returns></returns>
        public static bool TrimmedStringStorage(System.Type tp, string propname)
        {
            lock (cacheTrimmedStringStorage)
            {
                if (cacheTrimmedStringStorage[tp, propname] == null)
                {
                    int pointIndex = propname.IndexOf(".");
                    if (pointIndex >= 0)
                    {
                        string MasterName = propname.Substring(0, pointIndex);
                        string MasterPropName = propname.Substring(pointIndex + 1);
                        bool res = TrimmedStringStorage(GetPropertyType(tp, MasterName), MasterPropName);
                        cacheTrimmedStringStorage[tp, propname] = res;
                    }
                    else
                    {
                        PropertyInfo pi = tp.GetProperty(propname);
                        object[] atrs = pi.GetCustomAttributes(typeof(TrimmedStringStorageAttribute), true);
                        if (atrs.Length == 0)
                        {
                            atrs = tp.GetCustomAttributes(typeof(TrimmedStringStorageAttribute), true);
                            if (atrs.Length != 0)
                            {
                                if (((TrimmedStringStorageAttribute)atrs[0]).TrimmedStrings)
                                {
                                    cacheTrimmedStringStorage[tp, propname] = true;
                                }
                                else
                                {
                                    cacheTrimmedStringStorage[tp, propname] = false;
                                }
                            }
                            else
                            {
                                cacheTrimmedStringStorage[tp, propname] = false;
                            }
                        }
                        else
                            if (((TrimmedStringStorageAttribute)atrs[0]).TrimmedStrings)
                        {
                            cacheTrimmedStringStorage[tp, propname] = true;
                        }
                        else
                        {
                            cacheTrimmedStringStorage[tp, propname] = false;
                        }
                    }
                }

                return (bool)cacheTrimmedStringStorage[tp, propname];
            }
        }

        /// <summary>
        /// Установить значение свойства объекта данных по имени этого свойства,
        /// значение передаётся строкой.
        /// При установке свойства выполняется попытка преобразовать строковое значение
        /// в значение соответствующего типа путём вызова статического метода Parse(string)
        /// у этого типа.
        /// </summary>
        /// <param name="obj">Объект данных, значение свойства которого кстанавливается данным методом. </param>
        /// <param name="propName">Имя свойства объекта данных, значение которого устанавливается данным методом.</param>
        /// <param name="PropValue">Значение свойства объекта данных, которое будет установлено данным методом.</param>
        public static void SetPropValueByName(DataObject obj, string propName, string PropValue)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }

            if (propName == null)
            {
                throw new ArgumentNullException(nameof(propName));
            }

            try
            {
                int pointIndex = propName.IndexOf(".", StringComparison.Ordinal);
                if (pointIndex >= 0)
                {
                    string masterName = propName.Substring(0, pointIndex);
                    var masterValue = (DataObject)GetPropValueByName(obj, masterName);
                    propName = propName.Substring(pointIndex + 1);
                    SetPropValueByName(masterValue, propName, PropValue);
                }
                else
                {
                    if (PropValue == null)
                    {
                        SetPropValueByName(obj, propName, (object)PropValue);
                        return;
                    }

                    System.Type propType;
                    System.Type tp = obj.GetType();
                    PropertyInfo pi = tp.GetProperty(propName);
                    if (pi == null)
                    {
                        if (obj.DynamicProperties.ContainsKey(propName))
                        {
                            propType = typeof(object);
                        }
                        else
                        {
                            throw new CantFindPropertyException(propName, tp);
                        }
                    }
                    else
                    {
                        if (propName != "__PrimaryKey")
                        {
                            propType = GetPropertyType(tp, propName);
                        }
                        else
                        {
                            propType = KeyGen.KeyGenerator.KeyType(tp);
                        }
                    }

                    if (propType.IsGenericType && propType.GetGenericTypeDefinition() == typeof(Nullable<>))
                    {
                        if (PropValue == string.Empty)
                        {
                            // Сервис данных обрабатывает string.Empty как null-значение, так что будем присваивать его напрямую. Также это закрывает проблему с десериализацией объектов, когда null записан как string.Empty
                            SetPropValueByName(obj, propName, null);
                            return;
                        }

                        propType = Nullable.GetUnderlyingType(propType);
                    }

                    SetHandler setHandler = GetSetHandler(tp, pi);

                    if (propType == typeof(string))
                    {
                        if (pi.CanWrite)
                        {
                            setHandler(obj, PropValue);
                        }
                    }
                    else
                    {
                        if (pi == null && obj.DynamicProperties.ContainsKey(propName))
                        {
                            obj.DynamicProperties[propName] = PropValue;
                        }
                        else
                        {
                            if (pi.CanWrite)
                            {
                                object newPropVal;
                                string propValString = PropValue;
                                if (propType.IsEnum)
                                {
                                    propValString = propValString.Trim();
                                    newPropVal = EnumCaption.GetValueFor(propValString, propType);
                                    setHandler(obj, newPropVal);
                                }
                                else
                                {
                                    if (propType != typeof(object))
                                    {
                                        if (propType == typeof(DateTime))
                                        {
                                            DateTime dtVal;
                                            if (DateTime.TryParse(propValString, out dtVal))
                                            {
                                                setHandler(obj, dtVal);
                                                return;
                                            }

                                            IFormatProvider culture = new System.Globalization.CultureInfo("ru-RU", false);
                                            var dtVal1 = DateTime.Parse(propValString, culture);
                                            setHandler(obj, dtVal1);
                                            return;
                                        }

                                        if (propType == typeof(Geography))
                                        {
                                            WellKnownTextSqlFormatter wktFormatter = WellKnownTextSqlFormatter.Create();
                                            var geo = wktFormatter.Read<Geography>(new StringReader(propValString));
                                            setHandler(obj, geo);
                                            return;
                                        }

                                        if (propType == typeof(Geometry))
                                        {
                                            WellKnownTextSqlFormatter wktFormatter = WellKnownTextSqlFormatter.Create();
                                            var geo = wktFormatter.Read<Geometry>(new StringReader(propValString));
                                            setHandler(obj, geo);
                                            return;
                                        }

                                        if (propType.GetMethod("Parse", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public, null, new Type[] { typeof(string), typeof(System.IFormatProvider) }, null) != null)
                                        {
                                            try
                                            {
                                                newPropVal = propType.InvokeMember("Parse", System.Reflection.BindingFlags.InvokeMethod, null, null, new object[2] { propValString, System.Globalization.NumberFormatInfo.InvariantInfo });
                                                setHandler(obj, newPropVal);
                                                return;
                                            }
                                            catch
                                            { }
                                        }

                                        if (propType.GetMethod("Parse", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public, null, new Type[] { typeof(string) }, null) != null)
                                        {
                                            newPropVal = propType.InvokeMember("Parse", System.Reflection.BindingFlags.InvokeMethod, null, null, new object[1] { propValString });
                                            setHandler(obj, newPropVal);
                                        }
                                        else
                                        {
                                            MethodInfo opImpl = propType.GetMethod("op_Implicit", new Type[] { typeof(string) });
                                            if (opImpl != null && opImpl.IsSpecialName)
                                            {
                                                newPropVal = opImpl.Invoke(null, new object[] { propValString });
                                            }
                                            else
                                            {
                                                MethodInfo opExpl = propType.GetMethod("op_Explicit", new Type[] { typeof(string) });
                                                if (opExpl != null && opExpl.IsSpecialName)
                                                {
                                                    newPropVal = opExpl.Invoke(null, new object[] { propValString });
                                                }
                                                else
                                                {
                                                    throw new InvalidCastException();
                                                }
                                            }

                                            setHandler(obj, newPropVal);
                                        }
                                    }
                                    else
                                    {
                                        newPropVal = PropValue;
                                        setHandler(obj, newPropVal);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception(string.Format("Error on set {0} to property {1} of {2}", PropValue ?? "null", propName ?? string.Empty, obj == null ? "empty object" : obj.GetType().FullName), ex);
            }
        }

        private static Hashtable stTypesList = new Hashtable();

        /// <summary>
        /// Проверка: является ли переданный тип определённым в namespace <see cref="System"/>.
        /// </summary>
        /// <param name="type">Тип данных.</param>
        /// <returns><see langword="true" /> если тип является системным.</returns>
        private static bool IsSystemType(System.Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            lock (stTypesList)
            {
                object res = stTypesList[type];
                if (res == null)
                {
                    string name = type.FullName;
                    res = name.StartsWith("System.", StringComparison.Ordinal) && (name.IndexOf(".", 7, StringComparison.Ordinal) == -1);
                    stTypesList.Add(type, res);
                }

                return (bool)res;
            }
        }

        private static ConcurrentDictionary<long, SetHandler> cacheSetPropValueByName = new ConcurrentDictionary<long, SetHandler>();

        /// <summary>
        /// Получить делегат <see cref="SetHandler" /> для быстрого доступа к свойствам.
        /// </summary>
        /// <param name="type">Тип данных.</param>
        /// <param name="propInfo">Метаданные о свойстве.</param>
        /// <returns>Делегат.</returns>
        internal static SetHandler GetSetHandler(Type type, PropertyInfo propInfo)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (propInfo == null)
            {
                throw new ArgumentNullException(nameof(propInfo));
            }

            long key = (type.GetHashCode() * 10000000000) + propInfo.Name.GetHashCode();
            return cacheSetPropValueByName.GetOrAdd(key, k => DynamicMethodCompiler.CreateSetHandler(type, propInfo));
        }

        /// <summary>
        /// Получить делегат <see cref="SetHandler" /> для быстрого доступа к полям.
        /// </summary>
        /// <param name="type">Тип данных.</param>
        /// <param name="fieldInfo">Метаданные о поле.</param>
        /// <returns>Делегат.</returns>
        internal static SetHandler GetSetHandler(Type type, FieldInfo fieldInfo)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (fieldInfo == null)
            {
                throw new ArgumentNullException(nameof(fieldInfo));
            }

            long key = (type.GetHashCode() * 10000000000) + fieldInfo.Name.GetHashCode();
            return cacheSetPropValueByName.GetOrAdd(key, k => DynamicMethodCompiler.CreateSetHandler(type, fieldInfo));
        }

        /// <summary>
        /// Установить значение свойства объекта данных по имени этого свойства,
        /// значение передаётся типизированно. Если попытка преобразования
        /// типа неудачна, возвращается сообщение об ошибке.
        /// </summary>
        /// <param name="obj">Объект данных, значение свойства которого кстанавливается данным методом. </param>
        /// <param name="propName">Имя свойства объекта данных, значение которого устанавливается данным методом.</param>
        /// <param name="PropValue">Значение свойства объекта данных, которое будет установлено данным методом.</param>
        public static void SetPropValueByName(DataObject obj, string propName, object PropValue)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }

            if (propName == null)
            {
                throw new ArgumentNullException(nameof(propName));
            }

            try
            {
                int pointIndex = propName.IndexOf(".", StringComparison.Ordinal);
                if (pointIndex >= 0)
                {
                    string masterName = propName.Substring(0, pointIndex);
                    var masterValue = (DataObject)GetPropValueByName(obj, masterName);
                    propName = propName.Substring(pointIndex + 1);
                    SetPropValueByName(masterValue, propName, PropValue);
                }
                else
                {
                    if (PropValue == System.DBNull.Value)
                    {
                        PropValue = null;
                    }

                    if (PropValue is string value)
                    {
                        SetPropValueByName(obj, propName, value);
                    }
                    else
                    {
                        Type objType = obj.GetType();
                        PropertyInfo propInfo = objType.GetProperty(propName);
                        if (propInfo == null && obj.DynamicProperties.ContainsKey(propName))
                        {
                            obj.DynamicProperties[propName] = PropValue;
                        }
                        else
                        {
                            if (propInfo != null && propInfo.CanWrite)
                            {
                                System.Type propType = propInfo.PropertyType;

                                if (propType.IsGenericType && propType.GetGenericTypeDefinition() == typeof(Nullable<>))
                                {
                                    if (PropValue == string.Empty)
                                    {
                                        // Сервис данных обрабатывает string.Empty как null-значение, так что будем присваивать его напрямую. Также это закрывает проблему с десериализацией объектов, когда null записан как string.Empty
                                        SetPropValueByName(obj, propName, null);
                                        return;
                                    }

                                    propType = Nullable.GetUnderlyingType(propType);
                                }

                                SetHandler setHandler = GetSetHandler(objType, propInfo);
                                if (propType.IsEnum && PropValue == null)
                                {
                                    try
                                    {
                                        setHandler(obj, EnumCaption.GetValueFor(null, propType));
                                    }
                                    catch
                                    {
                                        propInfo.SetValue(obj, null, null);
                                    }
                                }
                                else if (PropValue == null)
                                {
                                    if (propType.IsValueType)
                                    {
                                        propInfo.SetValue(obj, null, null);
                                    }
                                    else
                                    {
                                        setHandler(obj, null);
                                    }
                                }
                                else
                                {
                                    Type valType = PropValue.GetType();

                                    if ((valType == propType)
                                        || (IsSystemType(valType) && IsSystemType(propType))
                                        || (propType == typeof(object))
                                        || valType.IsSubclassOf(propType))
                                    {
                                        object convertedValue;
                                        if (valType != propType && valType.GetInterface(nameof(IConvertible)) != null)
                                        {
                                            convertedValue = Convert.ChangeType(PropValue, propType);
                                        }
                                        else if (valType == typeof(byte[]) && propInfo.PropertyType == typeof(System.Guid) && (PropValue as byte[]).Length == 16)
                                        {
                                            convertedValue = new Guid(PropValue as byte[]);
                                        }
                                        else
                                        {
                                            convertedValue = PropValue;
                                        }

                                        setHandler(obj, convertedValue);
                                    }
                                    else if (propType == typeof(bool))
                                    {
                                        bool resisnum = false;
                                        try
                                        {
                                            var p = (int)PropValue;
                                            resisnum = true;
                                        }
                                        catch
                                        {
                                            resisnum = false;
                                        }

                                        if (resisnum)
                                        {
                                            setHandler(obj, ((int)PropValue) != 0);
                                        }
                                        else
                                        {
                                            if (!Convertors.InOperatorsConverter.CanConvert(valType, propType))
                                            {
                                                throw new InvalidCastException();
                                            }

                                            object convertedValue = Convertors.InOperatorsConverter.Convert(PropValue, propType);
                                            setHandler(obj, convertedValue);
                                        }
                                    }
                                    else
                                    {
                                        if (!Convertors.InOperatorsConverter.CanConvert(valType, propType))
                                        {
                                            throw new InvalidCastException();
                                        }

                                        object convertedValue = Convertors.InOperatorsConverter.Convert(PropValue, propType);
                                        setHandler(obj, convertedValue);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception(
                    string.Format(
                        "Error on set {0} to property {1} of {2}",
                        PropValue ?? "null",
                        propName ?? string.Empty,
                        obj == null ? "empty object" : obj.GetType().FullName),
                    ex);
            }
        }

        #endregion

        #region Работа с представлениями

        #region "View GetView(string ViewName,System.Type type)"

        /// <summary>
        /// кэш для функции GetView.
        /// </summary>
        private static ConcurrentDictionary<long, View> cacheGetView = new ConcurrentDictionary<long, View>();

        /// <summary>
        /// Делегат для настройки статических представлений.
        /// </summary>
        public static TuneStaticViewDelegate TuneStaticViewDelegate = null;

        /// <summary>
        /// Получить представление по его имени и классу объекта данных из кэша.
        /// </summary>
        /// <param name="viewName">Имя статического представления.</param>
        /// <param name="type">Тип данных.</param>
        /// <returns>Запрашиваемое представление, возможно из кеша.</returns>
        public static View GetView(string viewName, Type type)
        {
            if (string.IsNullOrEmpty(viewName))
            {
                throw new ArgumentNullException(nameof(viewName), "Не указано имя представления. Обратитесь к разработчику.");
            }

            if (type == null)
            {
                throw new ArgumentNullException(nameof(type), "Не указан тип объекта. Обратитесь к разработчику.");
            }

            long key = (((long)type.GetHashCode()) << 32) + viewName.GetHashCode();
            var view = cacheGetView.GetOrAdd(key, k => GetViewInternal(viewName, type));
            if (view == null)
            {
                return null;
            }

            View retView = view.Clone();
            if (TuneStaticViewDelegate != null)
            {
                retView = TuneStaticViewDelegate(viewName, type, retView);
            }

            return retView;
        }

        /// <summary>
        /// Clear cache for <see cref="GetView(string, Type)"/> method.
        /// </summary>
        public static void ClearCacheGetView()
        {
            cacheGetView.Clear();
        }

        /// <summary>
        /// Получить представление, "совместимое" с переданными классами.
        /// Ищет общего предка, затем пытается взять у него указанное представление.
        /// Если представление не найдено, возвращается null.
        /// </summary>
        /// <param name="ViewName">имя представления.</param>
        /// <param name="types">одномерный массив типов классов данных.</param>
        /// <returns></returns>
        public static View GetCompatibleView(string ViewName, System.Type[] types)
        {
            // ищем базовый класс
            System.Type testType = types[0];
            bool compAll = false;
            while (!compAll || testType == typeof(DataObject))
            {
                compAll = true;
                for (int i = 1; i < types.Length; i++)
                {
                    if (types[i] != testType && !types[i].IsSubclassOf(testType))
                    {
                        compAll = false;
                        break;
                    }
                }

                if (!compAll)
                {
                    testType = testType.BaseType;
                }
            }

            if (testType != typeof(DataObject))
            {
                return GetView(ViewName, testType);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Получить статическое представление по его имени и классу объекта данных.
        /// </summary>
        /// <param name="viewName">Имя статического представления.</param>
        /// <param name="type">Тип данных.</param>
        /// <returns>Представление.</returns>
        internal static View GetViewInternal(string viewName, Type type)
        {
            var classAttributes = type.GetCustomAttributes<ViewAttribute>(false);
            foreach (var attr in classAttributes)
            {
                string sViewName = attr.Name;
                if (sViewName == viewName)
                {
                    return new View(attr, type);
                }
            }

            return type.BaseType == null
                ? null
                : GetView(viewName, type.BaseType);
        }

        #endregion

        #region "string[] AllViews(System.Type type)"
        private static TypeAtrValueCollection cacheAllViews = new TypeAtrValueCollection();

        /// <summary>
        /// Получить список имён представлений для указанного класса объекта данных.
        /// </summary>
        /// <param name="type">Тип представления.</param>
        /// <returns>Массив строк, содержащих имена представлений для указанного типа.</returns>
        public static string[] AllViews(System.Type type)
        {
            lock (cacheAllViews)
            {
                string[] res = (string[])cacheAllViews[type];
                if (res != null)
                {
                    return CopyStringArray(res);
                }
                else
                {
                    object[] classAttributes = type.GetCustomAttributes(typeof(ViewAttribute), true);
                    ArrayList arl = new ArrayList(classAttributes.Length);
                    for (int i = 0; i < classAttributes.Length; i++)
                    {
                        string viewName = ((ViewAttribute)classAttributes[i]).Name;
                        if (!arl.Contains(viewName))
                        {
                            arl.Add(viewName);
                        }
                    }

                    string[] retval = new string[arl.Count];
                    arl.CopyTo(retval);
                    arl.Clear();
                    arl = null;
                    cacheAllViews[type] = retval;
                    return retval;
                }
            }
        }

        #endregion

        #region "string[] AllViews(params System.Type[] types)"

        /// <summary>
        /// Получить список имён общих представлений для указанных классов.
        /// Речь идёт о ситуации, когда образующие иерархию наследования классы
        /// имеют представления, что означает, что имеется множество представлений,
        /// общее для некоторого множества классов.
        /// Указывая в этот метод это множество классов, Вы и получите имена их общих представлений.
        /// </summary>
        public static string[] AllViews(params System.Type[] types)
        {
            if (types.Length == 0)
            {
                return new string[0];
            }
            else
            {
                string[] viewfortype = AllViews(types[0]);
                ArrayList res = new ArrayList(viewfortype.Length);
                for (int i = 0; i < viewfortype.Length; i++)
                {
                    if (CheckViewForClasses((string)viewfortype[i], types))
                    {
                        res.Add(viewfortype[i]);
                    }
                }

                string[] resarr = new string[res.Count];
                if (res.Count == 0)
                {
                    return resarr;
                }
                else
                {
                    res.CopyTo(resarr);
                    return resarr;
                }
            }
        }

        #endregion

        #region "bool CheckViewForClasses(string ViewName,params System.Type[] types)"

        /// <summary>
        /// Проверить, доступно ли указанное по имени представление во всех перечисленных классах.
        /// Речь идёт о ситуации, когда образующие иерархию наследования классы
        /// имеют представления, что означает, что имеется множество представлений,
        /// общее для некоторого множества классов.
        /// </summary>
        public static bool CheckViewForClasses(string ViewName, params System.Type[] types)
        {
            if (types.Length == 0)
            {
                return false;
            }

            View firstView = GetView(ViewName, types[0]);
            if (firstView == null)
            {
                return false;
            }

            for (int i = 1; i < types.Length; i++)
            {
                View curView = GetView(ViewName, types[i]);
                if (curView == null)
                {
                    return false;
                }

                if (curView.DefineClassType != firstView.DefineClassType)
                {
                    return false;
                }
            }

            return true;
        }

        #endregion

        /// <summary>
        /// Вернуть список всех встречающихся в представлении типов, включая детейлы.
        /// </summary>
        /// <param name="view">Представление.</param>
        /// <returns>Спосок типов без дублей.</returns>
        public static List<Type> GetAllTypesFromView(View view)
        {
            var result = new List<Type>();

            if (view == null || view.DefineClassType == null)
            {
                return result;
            }

            // Получим список типов свойств представления без дублей.
            result = view.Properties.Select(p => GetPropertyType(view.DefineClassType, p.Name)).Distinct().ToList();

            // Рекурсивно пробежимся по детейлам.
            foreach (var detail in view.Details)
            {
                var detailTypes = GetAllTypesFromView(detail.View);
                var uniqueDetailTypes = detailTypes.Except(result);
                result.AddRange(uniqueDetailTypes);
            }

            return result;
        }

        /// <summary>
        /// Вернуть список всех встречающихся в представлении типов, включая детейлы и псевдодетейлы.
        /// </summary>
        /// <param name="view">Расширенное представление (с псевдодетейлами).</param>
        /// <returns>Спосок типов без дублей.</returns>
        public static List<Type> GetAllTypesFromView(ExtendedView view)
        {
            if (view == null || string.IsNullOrEmpty(view.ViewName) || view.View == null)
            {
                return new List<Type>();
            }

            var types = new List<Type>();

            foreach (var currentProperty in view.ViewPropertiesOrderedList)
            {
                if (currentProperty is PropertyInView)
                {
                    types.Add(GetPropertyType(view.DefineClassType, ((PropertyInView)currentProperty).Name));
                }
                else if (currentProperty is DetailInView)
                {
                    var detail = (DetailInView)currentProperty;
                    var detailTypes = GetAllTypesFromView(detail.View);
                    types.AddRange(detailTypes);
                }
                else if (currentProperty is PseudoDetailInExtendedView)
                {
                    var pseudoDetail = (PseudoDetailInExtendedView)currentProperty;
                    var pseudoDetailView = GetView(pseudoDetail.PseudoDetailViewName, pseudoDetail.PseudoDetailType);
                    if (pseudoDetailView != null)
                    {
                        var pseudoDetailTypes = GetAllTypesFromView(pseudoDetailView);
                        var uniquePseudoDetailTypes = pseudoDetailTypes.Except(types);
                        types.AddRange(uniquePseudoDetailTypes);
                    }
                }
            }

            return types.Distinct().ToList();
        }

        #endregion

        #region Информация о свойствах

        private static TypeAtrValueCollection cacheGetTypeStorageName = new TypeAtrValueCollection();

        /// <summary>
        /// Имя хранилища для типа.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public static string GetTypeStorageName(System.Type type)
        {
            lock (cacheGetTypeStorageName)
            {
                object res = cacheGetTypeStorageName[type];
                if (res != null)
                {
                    return (string)res;
                }
                else
                {
                    object[] typeAttributes = type.GetCustomAttributes(typeof(TypeStorageAttribute), true);

                    if (typeAttributes.Length == 0)
                    {
                        res = type.Name; // "typeId"
                    }
                    else
                    {
                        res = ((TypeStorageAttribute)typeAttributes[0]).Name;
                    }

                    cacheGetTypeStorageName[type] = res;
                    return (string)res;
                }
            }
        }

        private static TypeAtrValueCollection cacheGetPrimaryKeyStorageName = new TypeAtrValueCollection();

        /// <summary>
        /// Получить имя хранения первичного ключа, установленное атрибутом <see cref="PrimaryKeyStorageAttribute"/>.
        /// </summary>
        /// <param name="type">.Net-тип класса объекта данных.</param>
        /// <returns>имя хранения первичного ключа.</returns>
        public static string GetPrimaryKeyStorageName(System.Type type)
        {
            lock (cacheGetPrimaryKeyStorageName)
            {
                object res = cacheGetPrimaryKeyStorageName[type];
                if (res != null)
                {
                    return (string)res;
                }
                else
                {
                    object[] typeAttributes = type.GetCustomAttributes(typeof(PrimaryKeyStorageAttribute), true);

                    if (typeAttributes.Length == 0)
                    {
                        res = "primaryKey";
                    }
                    else
                    {
                        res = ((PrimaryKeyStorageAttribute)typeAttributes[0]).Name;
                    }

                    cacheGetPrimaryKeyStorageName[type] = res;
                    return (string)res;
                }
            }
        }

        private static TypePropertyAtrValueCollection cacheGetCompatibleTypesForProperty = new TypePropertyAtrValueCollection();

        /// <summary>
        /// Возвращает типы, совместимые с данным свойством(по TypeUsage).
        /// </summary>
        /// <param name="type"></param>
        /// <param name="property"></param>
        /// <returns></returns>
        public static System.Type[] GetCompatibleTypesForProperty(System.Type type, string property)
        {
            lock (cacheGetCompatibleTypesForProperty)
            {
                System.Type[] res = (System.Type[])cacheGetCompatibleTypesForProperty[type, property];
                if (res != null)
                {
                    return CopyTypeArray(res);
                }
                else
                {
                    int pointIndex = property.IndexOf(".");
                    if (pointIndex >= 0)
                    {
                        string MasterName = property.Substring(0, pointIndex);
                        string MasterPropName = property.Substring(pointIndex + 1);
                        res = GetCompatibleTypesForProperty(GetPropertyType(type, MasterName), MasterPropName);
                        cacheGetCompatibleTypesForProperty[type, property] = res;
                    }
                    else
                    {
                        object[] classAtrs = type.GetCustomAttributes(typeof(PropertyTypeUsageAttribute), false);

                        if (classAtrs.Length > 0)
                        {
                            for (int i = 0; i < classAtrs.Length; i++)
                            {
                                var atr = (PropertyTypeUsageAttribute)classAtrs[i];
                                if (atr.Property == property)
                                {
                                    res = atr.UseTypes;
                                    break;
                                }
                            }
                        }

                        if (res == null)
                        {
                            PropertyInfo pi = type.GetProperty(property);
                            var atrs = pi.GetCustomAttributes(typeof(TypeUsageAttribute), true);
                            res = atrs.Length > 0 ? ((TypeUsageAttribute)atrs[0]).UseTypes : new Type[1] { pi.PropertyType };
                            cacheGetCompatibleTypesForProperty[type, property] = res;
                        }
                    }

                    return CopyTypeArray(res);
                }
            }
        }

        private static TypePropertyAtrValueCollection cacheGetItemType = new TypePropertyAtrValueCollection();

        /// <summary>
        /// Возвращает тип элемента DetailArray.
        /// </summary>
        /// <param name="agregatorType">объект-владелец.</param>
        /// <param name="detailPropertyName">свойство-DetailArray.</param>
        /// <returns>net-тип элемента DetailArray.</returns>
        public static Type GetItemType(Type agregatorType, string detailPropertyName)
        {
            if (agregatorType == null)
            {
                throw new ArgumentNullException(nameof(agregatorType));
            }

            if (detailPropertyName == null)
            {
                throw new ArgumentNullException(nameof(detailPropertyName));
            }

            lock (cacheGetItemType)
            {
                Type res = (Type)cacheGetItemType[agregatorType, detailPropertyName];
                if (res != null)
                {
                    return res;
                }

                int pointIndex = detailPropertyName.IndexOf(".", StringComparison.Ordinal);
                if (pointIndex >= 0)
                {
                    string masterName = detailPropertyName.Substring(0, pointIndex);
                    detailPropertyName = detailPropertyName.Substring(pointIndex + 1);
                    Type masterType = GetPropertyType(agregatorType, masterName);
                    if (masterType == null)
                    {
                        throw new CantFindPropertyException(masterName, agregatorType);
                    }

                    res = GetItemType(masterType, detailPropertyName);
                }
                else
                {
                    string err = string.Empty;

                    try
                    {
                        Type propType = GetPropertyType(agregatorType, detailPropertyName);
                        if (propType.IsSubclassOf(typeof(DetailArray)))
                        {
                            ConstructorInfo ci = null;
                            err = string.Empty;
                            ConstructorInfo[] constructorInfos = propType.GetConstructors();
                            foreach (ConstructorInfo cci in constructorInfos)
                            {
                                ParameterInfo[] pars = cci.GetParameters();
                                if (pars.Length == 1)
                                {
                                    err += cci.ToString() + " " + Environment.NewLine +
                                           pars[0].ParameterType.AssemblyQualifiedName + Environment.NewLine +
                                           agregatorType.AssemblyQualifiedName + Environment.NewLine +
                                           (pars[0].ParameterType == agregatorType).ToString() +
                                           ";";
                                    if ((pars[0].ParameterType == agregatorType) || agregatorType.IsSubclassOf(pars[0].ParameterType))
                                    {
                                        ci = cci;
                                        break;
                                    }
                                }
                            }

                            if (ci != null)
                            {
                                DetailArray da = (DetailArray)ci.Invoke(new object[] { null });
                                res = da.ItemType;
                            }
                            else
                            {
                                err = "Cant find constructor " + err;
                                throw new Exception(err);
                            }
                        }
                    }
                    catch
                    {
                        throw new Exception("Information getItemType(" + ((agregatorType == null) ? "NULL" : agregatorType.FullName) + "," + detailPropertyName + ")" +
                                            Environment.NewLine + err);
                    }
                }

                // Для генерённых на ходу типов не добавляем в кеш, т.к. они меняются в любой момент (например редактор параметров генерит фиктивный тип для задания параметров и формы параметров)
                if (agregatorType.Assembly.FullName != "TempAssembly, Version=0.0.0.0")
                {
                    cacheGetItemType[agregatorType, detailPropertyName] = res;
                }

                return res;
            }
        }

        private static TypePropertyAtrValueCollection cacheGetCompatibleTypesForDetailProperty = new TypePropertyAtrValueCollection();

        /// <summary>
        ///  возвращает типы, совместимые с детейловым свойством(по TypeUsage).
        /// </summary>
        /// <param name="type"></param>
        /// <param name="property"></param>
        /// <returns></returns>
        public static System.Type[] GetCompatibleTypesForDetailProperty(System.Type type, string property)
        {
            lock (cacheGetCompatibleTypesForDetailProperty)
            {
                System.Type[] res = (Type[])cacheGetCompatibleTypesForDetailProperty[type, property];
                if (res != null)
                {
                    return CopyTypeArray(res);
                }

                int pointIndex = property.IndexOf(".");
                if (pointIndex >= 0)
                {
                    string MasterName = property.Substring(0, pointIndex);
                    property = property.Substring(pointIndex + 1);
                    System.Type MasterType = GetPropertyType(type, MasterName);
                    if (MasterType == null)
                    {
                        throw new CantFindPropertyException(property, type);
                    }

                    res = GetCompatibleTypesForDetailProperty(MasterType, property);
                    return res;
                }

                var pi = type.GetProperty(property);
                var cp = GetCompatibleTypesForProperty(type, property);
                Type proptype = pi.PropertyType;
                if (cp[0] == proptype)
                {
                    if (proptype.IsSubclassOf(typeof(DetailArray)))
                    {
                        object[] atrs = proptype.GetCustomAttributes(typeof(TypeUsageAttribute), true);
                        if (atrs.Length > 0)
                        {
                            res = ((TypeUsageAttribute)atrs[0]).UseTypes;
                            cacheGetCompatibleTypesForDetailProperty[type, property] = res;
                            return res;
                        }

                        res = new System.Type[] { GetItemType(type, property) };
                        cacheGetCompatibleTypesForDetailProperty[type, property] = res;
                        return res;
                    }

                    return null;
                }

                cacheGetCompatibleTypesForDetailProperty[type, property] = cp;
                return cp;
            }
        }

        private static TypePropertyAtrValueCollection cacheGetPropertyDisableAutoViewing = new TypePropertyAtrValueCollection();

        /// <summary>
        /// Вернуть является ли свойство автоматически включаемым в представления.
        /// </summary>
        /// <param name="type">тип.</param>
        /// <param name="property">свойство.</param>
        /// <returns></returns>
        public static bool GetPropertyDisableAutoViewing(Type type, string property)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (property == null)
            {
                throw new ArgumentNullException(nameof(property));
            }

            lock (cacheGetPropertyDisableAutoViewing)
            {
                var res = cacheGetPropertyDisableAutoViewing[type, property];
                if (res != null)
                {
                    return (bool)res;
                }

                int pointIndex = property.IndexOf(".", StringComparison.Ordinal);
                if (pointIndex >= 0)
                {
                    string masterName = property.Substring(0, pointIndex);
                    property = property.Substring(pointIndex + 1);
                    Type masterType = GetPropertyType(type, masterName);
                    if (masterType == null)
                    {
                        throw new CantFindPropertyException(property, type);
                    }

                    res = GetPropertyDisableAutoViewing(masterType, property);
                }
                else
                {
                    PropertyInfo pi = type.GetProperty(property);
                    if (pi == null)
                    {
                        throw new CantFindPropertyException(property, type);
                    }

                    var typeAttribute = pi.GetCustomAttribute<DisableAutoViewedAttribute>(true);
                    if (typeAttribute == null)
                    {
                        res = false;
                    }
                    else
                    {
                        res = typeAttribute.value;
                    }
                }

                cacheGetPropertyDisableAutoViewing[type, property] = res;
                return (bool)res;
            }
        }

        private static TypePropertyAtrValueCollection cacheGetPropertyStorageName = new TypePropertyAtrValueCollection();

        /// <summary>
        /// Получить имя хранения .Net-свойства, установленное атрибутом <see cref="PropertyStorageAttribute"/>.
        /// </summary>
        /// <param name="type">.Net-тип класса объекта данных.</param>
        /// <param name="property">имя свойства.</param>
        /// <returns>имя хранения.</returns>
        public static string GetPropertyStorageName(System.Type type, string property)
        {
            lock (cacheGetPropertyStorageName)
            {
                object res = cacheGetPropertyStorageName[type, property];
                if (res != null)
                {
                    return (string)res;
                }
                else
                {
                    string resstr = string.Empty;
                    if (property == "__PrimaryKey")
                    {
                        resstr = GetPrimaryKeyStorageName(type);
                    }
                    else
                    {
                        PropertyInfo pi = type.GetProperty(property);
                        if (pi == null)
                        {
                            throw new CantFindPropertyException(property, type);
                        }

                        object[] typeAttributes = pi.GetCustomAttributes(typeof(PropertyStorageAttribute), true);
                        if (typeAttributes.Length == 0)
                        {
                            resstr = property;
                        }
                        else
                        {
                            resstr = ((PropertyStorageAttribute)typeAttributes[0]).Name;
                        }
                    }

                    cacheGetPropertyStorageName[type, property] = resstr;
                    return resstr;
                }
            }
        }

        private static TypePropertyAtrValueCollection cacheGetpropertyCaption = new TypePropertyAtrValueCollection();

        /// <summary>
        /// Вернуть заголовок свойства.
        /// </summary>
        /// <param name="type">тип.</param>
        /// <param name="property">свойство.</param>
        /// <returns></returns>
        public static string GetPropertyCaption(System.Type type, string property)
        {
            lock (cacheGetpropertyCaption)
            {
                object res = cacheGetpropertyCaption[type, property];
                if (res != null)
                {
                    return (string)res;
                }
                else
                {
                    string resstr = string.Empty;
                    int pointIndex = property.IndexOf(".");
                    if (pointIndex >= 0)
                    {
                        string MasterName = property.Substring(0, pointIndex);
                        string mpropname = property.Substring(pointIndex + 1);
                        System.Type MasterType = GetPropertyType(type, MasterName);
                        if (MasterType == null)
                        {
                            throw new CantFindPropertyException(MasterName, type);
                        }
                        else
                        {
                            resstr = GetPropertyCaption(MasterType, mpropname);
                        }
                    }
                    else
                    {
                        PropertyInfo pi = type.GetProperty(property);
                        if (pi == null)
                        {
                            throw new CantFindPropertyException(property, type);
                        }

                        object[] typeAttributes = pi.GetCustomAttributes(typeof(CaptionAttribute), true);
                        if (typeAttributes.Length == 0)
                        {
                            resstr = property;
                        }
                        else
                        {
                            resstr = ((CaptionAttribute)typeAttributes[0]).Value;
                        }
                    }

                    cacheGetpropertyCaption[type, property] = resstr;
                    return resstr;
                }
            }
        }

        private static TypePropertyAtrValueCollection cachePropertyStorageNameIndexed = new TypePropertyAtrValueCollection();

        /// <summary>
        /// Получить имя хранения .Net-свойства, установленное атрибутом <see cref="PropertyStorageAttribute"/>.
        /// </summary>
        /// <param name="type">.Net-тип класса объекта данных.</param>
        /// <param name="property">имя свойства.</param>
        /// <param name="index">индекс в множественном.</param>
        /// <returns>имя хранения.</returns>
        public static string GetPropertyStorageName(System.Type type, string property, int index)
        {
            lock (cachePropertyStorageNameIndexed)
            {
                object res = cachePropertyStorageNameIndexed[type, property + "/" + index.ToString()];
                if (res != null)
                {
                    return (string)res;
                }
                else
                {
                    res = cachePropertyStorageNameIndexed[type, property];
                    if (res != null)
                    {
                        return (string)res;
                    }
                    else
                    {
                        if (property == "__PrimaryKey")
                        {
                            string resstr = GetPrimaryKeyStorageName(type);
                            cachePropertyStorageNameIndexed[type, property] = resstr;
                            return resstr;
                        }
                        else
                        {
                            PropertyInfo pi = type.GetProperty(property);
                            if (pi == null)
                            {
                                throw new CantFindPropertyException(property, type);
                            }

                            object[] typeAttributes = pi.GetCustomAttributes(typeof(PropertyStorageAttribute), true);
                            if (typeAttributes.Length == 0)
                            {
                                cachePropertyStorageNameIndexed[type, property] = property;
                                return property;
                            }
                            else
                            {
                                string[] resarr = ((PropertyStorageAttribute)typeAttributes[0]).Names;
                                for (int i = 0; i < resarr.Length; i++)
                                {
                                    cachePropertyStorageNameIndexed[type, property + "/" + i.ToString()] = resarr[i];
                                }

                                return resarr[index];
                            }
                        }
                    }
                }
            }
        }

        private static TypePropertyAtrValueCollection cacheGetPropertyNotNull = new TypePropertyAtrValueCollection();

        /// <summary>
        /// Проверить, установлен ли для указанного .Net-свойства атрибут <see cref="NotNullAttribute"/>.
        /// </summary>
        /// <param name="type">.Net-тип класса объекта данных.</param>
        /// <param name="property">имя свойства.</param>
        /// <returns>true, если установлен, иначе false.</returns>
        public static bool GetPropertyNotNull(System.Type type, string property)
        {
            lock (cacheGetPropertyNotNull)
            {
                object res = cacheGetPropertyNotNull[type, property];
                if (res != null)
                {
                    return (bool)res;
                }
                else
                {
                    bool resbool;
                    int pointIndex = property.IndexOf(".");
                    if (pointIndex >= 0)
                    {
                        string MasterName = property.Substring(0, pointIndex);
                        string MasterPropName = property.Substring(pointIndex + 1);
                        resbool = GetPropertyNotNull(GetPropertyType(type, MasterName), MasterPropName);
                    }
                    else
                    {
                        PropertyInfo pi = type.GetProperty(property);
                        if (pi == null)
                        {
                            throw new CantFindPropertyException(property, type);
                        }

                        object[] typeAttributes = pi.GetCustomAttributes(typeof(NotNullAttribute), true);
                        if (typeAttributes.Length == 0)
                        {
                            resbool = false;
                        }
                        else
                        {
                            resbool = ((NotNullAttribute)typeAttributes[0]).NotNull;
                        }
                    }

                    cacheGetPropertyNotNull[type, property] = resbool;
                    return resbool;
                }
            }
        }

        private static TypePropertyAtrValueCollection cacheGetPropertyStrLen = new TypePropertyAtrValueCollection();

        /// <summary>
        /// Получить для указанного .Net-свойства атрибут <see cref="StrLenAttribute"/>.
        /// </summary>
        /// <param name="type">.Net-тип класса объекта данных.</param>
        /// <param name="property">имя свойства.</param>
        /// <returns>Значение установленного атрибута (-1 если не установлено).</returns>
        public static int GetPropertyStrLen(System.Type type, string property)
        {
            lock (cacheGetPropertyStrLen)
            {
                object resCache = cacheGetPropertyStrLen[type, property];
                if (resCache != null)
                {
                    return (int)resCache;
                }
                else
                {
                    int resInt = -1;
                    int pointIndex = property.IndexOf(".");
                    if (pointIndex >= 0)
                    {
                        string MasterName = property.Substring(0, pointIndex);
                        string MasterPropName = property.Substring(pointIndex + 1);
                        resInt = GetPropertyStrLen(GetPropertyType(type, MasterName), MasterPropName);
                    }
                    else
                    {
                        PropertyInfo pi = type.GetProperty(property);
                        if (pi == null)
                        {
                            throw new CantFindPropertyException(property, type);
                        }

                        object[] typeAttributes = pi.GetCustomAttributes(typeof(StrLenAttribute), true);
                        if (typeAttributes.Length == 0)
                        {
                            resInt = -1;
                        }
                        else
                        {
                            resInt = ((StrLenAttribute)typeAttributes[0]).StrLen;
                        }
                    }

                    cacheGetPropertyStrLen[type, property] = resInt;
                    return resInt;
                }
            }
        }

        /// <summary>
        /// Проверить, нет ли непустых значений в NotNull .Net-свойствах.
        /// </summary>
        /// <param name="dataObject">объект данных.</param>
        /// <returns>возвращает null, если непустых значений нет,
        /// иначе одномерный строковый массив с именами свойств, где значения есть.</returns>
        public static string[] CheckNotNullAttributes(DataObject dataObject)
        {
            Type dataobjtype = dataObject.GetType();
            ArrayList result = new ArrayList();
            string[] propnames = GetAllPropertyNames(dataobjtype);
            foreach (string propname in propnames)
            {
                if (GetPropertyNotNull(dataobjtype, propname) && GetPropValueByName(dataObject, propname) == null)
                {
                    result.Add(propname);
                }
            }

            if (result.Count == 0)
            {
                return null;
            }
            else
            {
                return (string[])result.ToArray();
            }
        }

        private static TypePropertyAtrValueCollection cacheDefinePropertyClassType = new TypePropertyAtrValueCollection();

        /// <summary>
        /// Вернуть тип в котором определено свойство.
        /// </summary>
        /// <param name="declarationType">исходный тип.</param>
        /// <param name="propname">исходное имя свойства.</param>
        /// <returns>тип в котором определено свойство.</returns>
        public static Type GetPropertyDefineClassType(System.Type declarationType, string propname)
        {
            lock (cacheDefinePropertyClassType)
            {
                Type res = null;
                res = (System.Type)cacheDefinePropertyClassType[declarationType, propname];
                if (res != null)
                {
                    return res;
                }
                else
                {
                    int pointIndex = propname.IndexOf(".");
                    if (pointIndex >= 0)
                    {
                        string MasterName = propname.Substring(0, pointIndex);
                        string mpropname = propname.Substring(pointIndex + 1);
                        System.Type MasterType = GetPropertyType(declarationType, MasterName);
                        if (MasterType == null)
                        {
                            throw new CantFindPropertyException(MasterName, declarationType);
                        }
                        else
                        {
                            res = GetPropertyDefineClassType(MasterType, mpropname);
                        }
                    }
                    else
                    {
                        PropertyInfo pi = declarationType.GetProperty(propname);
                        if (pi == null)
                        {
                            throw new CantFindPropertyException(propname, declarationType);
                        }

                        if (propname == "__PrimaryKey")
                        {
                            res = typeof(DataObject);
                        }
                        else
                        {
                            System.Type ptype = pi.DeclaringType;
                            res = ptype;
                        }
                    }

                    // Для генерённых на ходу типов не добавляем в кеш, т.к. они меняются в любой момент (например редактор параметров генерит фиктивный тип для задания параметров и формы параметров)
                    if (declarationType.Assembly.FullName != "TempAssembly, Version=0.0.0.0")
                    {
                        cacheDefinePropertyClassType[declarationType, propname] = res;
                    }

                    return res;
                }
            }
        }

        private static TypeAtrValueCollection cacheGetCompatibleTypesForTypeConvertion = new TypeAtrValueCollection();

        /// <summary>
        /// Куда можно мконвертировать тип.
        /// </summary>
        /// <param name="type">из чего.</param>
        /// <returns>куда.</returns>
        public static Type[] GetCompatibleTypesForTypeConvertion(Type type)
        {
            lock (cacheGetCompatibleTypesForTypeConvertion)
            {
                if (type == typeof(DataObject) || type == typeof(object))
                {
                    return Type.EmptyTypes;
                }

                Type[] res = null;
                res = (Type[])cacheGetCompatibleTypesForTypeConvertion[type];
                if (res != null)
                {
                    return res;
                }
                else
                {
                    ArrayList nr = new ArrayList();
                    nr.AddRange(GetCompatibleTypesForTypeConvertion(type.BaseType));
                    nr.Add(type);
                    res = (Type[])nr.ToArray(typeof(Type));
                    cacheGetCompatibleTypesForTypeConvertion[type] = res;
                    return res;
                }
            }
        }

        private static TypePropertyAtrValueCollection cachePropertyType = new TypePropertyAtrValueCollection();

        /// <summary>
        /// Получить .Net-тип свойства класса объекта данных по имени этого свойства.
        /// </summary>
        /// <param name="declarationType">.Net-тип класса объекта данных.</param>
        /// <param name="propname">имя свойства.</param>
        /// <returns>.Net-тип свойства.</returns>
        public static Type GetPropertyType(Type declarationType, string propname)
        {
            if (declarationType == null)
            {
                throw new ArgumentNullException(nameof(declarationType));
            }

            if (propname == null)
            {
                throw new ArgumentNullException(nameof(propname));
            }

            lock (cachePropertyType)
            {
                Type res = (Type)cachePropertyType[declarationType, propname];
                if (res != null)
                {
                    return res;
                }

                int pointIndex = propname.IndexOf(".", StringComparison.Ordinal);
                if (pointIndex >= 0)
                {
                    string masterName = propname.Substring(0, pointIndex);
                    string mpropname = propname.Substring(pointIndex + 1);
                    Type masterType = GetPropertyType(declarationType, masterName);
                    if (masterType.IsSubclassOf(typeof(DetailArray)))
                    {
                        masterType = GetItemType(declarationType, masterName);
                    }

                    if (masterType == null)
                    {
                        throw new CantFindPropertyException(masterName, declarationType);
                    }

                    res = GetPropertyType(masterType, mpropname);
                }
                else
                {
                    PropertyInfo pi = declarationType.GetProperty(propname);
                    if (pi == null)
                    {
                        throw new CantFindPropertyException(propname, declarationType);
                    }

                    if (propname == "__PrimaryKey")
                    {
                        res = KeyGen.KeyGenerator.KeyType(declarationType);
                    }
                    else
                    {
                        res = pi.PropertyType;
                    }
                }

                // Для генерённых на ходу типов не добавляем в кеш, т.к. они меняются в любой момент (например редактор параметров генерит фиктивный тип для задания параметров и формы параметров)
                if (declarationType.Assembly.FullName != "TempAssembly, Version=0.0.0.0")
                {
                    cachePropertyType[declarationType, propname] = res;
                }

                return res;
            }
        }

        /// <summary>
        /// ??????????????????.
        /// </summary>
        /// <param name="declarationType"></param>
        /// <param name="propname"></param>
        /// <param name="masterpref"></param>
        /// <param name="masterTypes"></param>
        /// <returns></returns>
        public static Type GetPropertyType(System.Type declarationType, string propname, string masterpref, Collections.NameObjectCollection masterTypes)
        {
            int pointIndex = propname.IndexOf(".", StringComparison.Ordinal);
            if (masterTypes != null && masterTypes.Count > 0)
            {
                System.Type MasterType = (masterTypes == null) ? null : (Type)masterTypes[propname];
                if (MasterType != null)
                {
                    return MasterType;
                }

                if (pointIndex > 0)
                {
                    string MasterName = propname.Substring(0, pointIndex);
                    string rMasterName = (masterpref == string.Empty) ? MasterName : masterpref + "." + MasterName;
                    if (MasterType != null)
                    {
                        return MasterType;
                    }
                }
            }

            Type res = null;
            if (pointIndex >= 0)
            {
                string MasterName = propname.Substring(0, pointIndex);
                string rMasterName = (masterpref == string.Empty) ? MasterName : masterpref + "." + MasterName;
                string mpropname = propname.Substring(pointIndex + 1);
                System.Type MasterType = GetPropertyType(declarationType, MasterName);
                if (MasterType == null)
                {
                    throw new CantFindPropertyException(propname, declarationType);
                }
                else
                {
                    res = GetPropertyType(MasterType, mpropname, rMasterName, masterTypes);
                }
            }
            else
            {
                PropertyInfo pi = declarationType.GetProperty(propname);
                if (pi == null)
                {
                    throw new CantFindPropertyException(propname, declarationType);
                }

                if (propname == "__PrimaryKey")
                {
                    res = KeyGen.KeyGenerator.KeyType(declarationType);
                }
                else
                {
                    System.Type ptype = pi.PropertyType;
                    res = ptype;
                }
            }

            return res;
        }

        /// <summary>
        /// Возвращает ???.
        /// </summary>
        public delegate string[] GetPropertiesInExpressionDelegate(string expression, string namespacewithpoint);

        /// <summary>
        /// Вернуть структуру хранения для представления.
        /// </summary>
        /// <param name="view">Пердставление <see cref="View"/>.</param>
        /// <param name="type"></param>
        /// <param name="storageType">Тип хранилища <see cref="Business.StorageTypeEnum"/>.</param>
        /// <param name="getPropertiesInExpression"></param>
        /// <param name="DataServiceType">Тип сервиса данных.</param>
        /// <returns></returns>
        public static Business.StorageStructForView GetStorageStructForView(
            View view,
            System.Type type,
            Business.StorageTypeEnum storageType,
            GetPropertiesInExpressionDelegate getPropertiesInExpression,
            System.Type DataServiceType)
        {
            switch (storageType)
            {
                case Business.StorageTypeEnum.SimpleStorage:
                    return GetSimpleStorageStructForView(view, type, getPropertiesInExpression, DataServiceType);
                case Business.StorageTypeEnum.HierarchicalStorage:
                    return GetHierarchicalStorageStructForView(view, type, getPropertiesInExpression, DataServiceType);
                default:
                    return null;
            }
        }

        /// <summary>
        /// Получить структуру хранения данных в соответствии
        /// с указанным представлением указанного .Net-типа класса объекта данных.
        /// </summary>
        /// <param name="view">представление.</param>
        /// <param name="type">.Net-тип класса объекта данных.</param>
        /// <param name="getPropertiesInExpression"></param>
        /// <param name="DataServiceType">тип сервиса данных.</param>
        /// <returns></returns>
        private static Business.StorageStructForView GetSimpleStorageStructForView(View view, System.Type type, GetPropertiesInExpressionDelegate getPropertiesInExpression, System.Type DataServiceType)
        {
            if (type != view.DefineClassType && !type.IsSubclassOf(view.DefineClassType))
            {
                throw new ClassIsNotSubclassOfOtherException(type, view.DefineClassType);
            }

            var retVal = new Business.StorageStructForView();
            var props = new List<Business.StorageStructForView.PropStorage>();
            retVal.sources.storage[0].Storage = GetClassStorageName(type);
            retVal.sources.storage[0].PrimaryKeyStorageName = GetPrimaryKeyStorageName(type);
            retVal.sources.storage[0].TypeStorageName = GetTypeStorageName(type);
            retVal.sources.storage[0].ownerType = type;
            retVal.sources.ObjectLink = string.Empty;

            retVal.sources.Name = view.DefineClassType.Name;
            var addedProperties = new StringCollection();

            int propsCount = view.Properties.Length;
            foreach (PropertyInView curprop in view.Properties)
            {
                propsCount--;
                var prop = new Business.StorageStructForView.PropStorage();
                prop.AdditionalProp = propsCount < 0;
                props.Add(prop);
                if (!addedProperties.Contains(curprop.Name))
                {
                    addedProperties.Add(curprop.Name);
                    prop.MultipleProp = false;
                }
                else
                {
                    prop.MultipleProp = true;
                }

                string scurpropnamepart = string.Empty;
                string curpropName = curprop.Name.Replace("." + nameof(DataObject.__PrimaryKey), string.Empty);
                string[] propname = curpropName.Split('.');
                Type propType = null;
                System.Type p = type;
                Business.StorageStructForView.PropSource curSource = retVal.sources;

                for (int j = 0; j < propname.Length; j++)
                {
                    string scrupropnamepref;
                    if (j == 0)
                    {
                        scurpropnamepart = propname[0];
                        scrupropnamepref = string.Empty;
                    }
                    else
                    {
                        scrupropnamepref = scurpropnamepart;
                        scurpropnamepart = scurpropnamepart + "." + propname[j];
                    }

                    if (j == propname.Length - 1)
                    {
                        propType = GetPropertyType(p, propname[j]);
                    }
                    else
                    {
                        bool propIsNotNull = GetPropertyNotNull(p, propname[j]);

                        bool found = false;
                        Business.StorageStructForView.PropSource nextSource = null;

                        string newStorageAlias = curSource.Name + propname[j];
                        for (int index = 0; index < curSource.LinckedStorages.Length; index++)
                        {
                            if (curSource.LinckedStorages[index].Name == newStorageAlias)
                            {
                                found = true;
                                nextSource = curSource.LinckedStorages[index];
                                break;
                            }
                        }

                        if (!found)
                        {
                            var newSources = new Business.StorageStructForView.PropSource[curSource.LinckedStorages.Length + 1];
                            nextSource = new Business.StorageStructForView.PropSource();
                            nextSource.Name = newStorageAlias;
                            nextSource.ObjectLink = propname[j];

                            for (int l = 0; l < curSource.storage.Length; l++)
                            {
                                // ***
                                System.Type filterType = (Type)view.MasterTypeFilters[scurpropnamepart];
                                if (filterType == null)
                                {
                                    filterType = typeof(DataObject);
                                }

                                var colMasterTypes = new ICSSoft.STORMNET.Collections.TypeBaseCollection();

                                // ***

                                System.Type[] masterTypes = TypeUsageProvider.TypeUsage.GetUsageTypes(curSource.storage[l].ownerType, propname[j]);

                                // ***
                                for (int k = 0; k < masterTypes.Length; k++)
                                {
                                    Type t = masterTypes[k];
                                    if (t == filterType || t.IsSubclassOf(filterType))
                                    {
                                        string storname = GetPropertyStorageName(curSource.storage[l].ownerType, nextSource.ObjectLink);
                                        if (storname == string.Empty)
                                        {
                                            storname = GetPropertyStorageName(curSource.storage[l].ownerType, nextSource.ObjectLink, k);
                                        }
                                        else
                                        {
                                            storname = storname + "_m" + k.ToString();
                                        }

                                        colMasterTypes.Add(t, storname);
                                    }
                                }

                                // ***
                                if (colMasterTypes.Count == 0)
                                {
                                    return null;
                                }

                                Business.StorageStructForView.ClassStorageDef[] tempArr;
                                if (nextSource.storage.Length == 1 && nextSource.storage[0].PrimaryKeyStorageName == null)
                                {
                                    tempArr = new Business.StorageStructForView.ClassStorageDef[0];
                                }
                                else
                                {
                                    tempArr = nextSource.storage;
                                }

                                nextSource.storage = new Business.StorageStructForView.ClassStorageDef[colMasterTypes.Count + tempArr.Length];
                                tempArr.CopyTo(nextSource.storage, 0);

                                for (int k = 0; k < colMasterTypes.Count; k++)
                                {
                                    // ****
                                    int kindex = k + tempArr.Length;
                                    System.Type mtype = colMasterTypes.Key(k);
                                    string storname = (string)colMasterTypes[k];

                                    // ****
                                    nextSource.storage[kindex].Storage = GetClassStorageName(mtype);
                                    nextSource.storage[kindex].PrimaryKeyStorageName = GetPrimaryKeyStorageName(mtype);
                                    nextSource.storage[kindex].TypeStorageName = GetTypeStorageName(mtype);
                                    nextSource.storage[kindex].ownerType = mtype;
                                    nextSource.storage[kindex].nullableLink = !propIsNotNull || masterTypes.Length > 0;
                                    nextSource.storage[kindex].objectLinkStorageName = storname;
                                    nextSource.storage[kindex].parentStorageindex = l;
                                }

                                curSource.LinckedStorages.CopyTo(newSources, 0);
                            }

                            newSources[newSources.Length - 1] = nextSource;
                            curSource.LinckedStorages = newSources;
                        }

                        p = GetPropertyType(p, propname[j], scrupropnamepref, view.MasterTypeFilters);
                        curSource = nextSource;
                    }
                }

                prop.source = curSource;
                prop.storage = new string[curSource.storage.Length][];
                prop.propertyType = propType;
                string pname = propname[propname.Length - 1];

                bool propertyIsMaster = GetPropertyType(curSource.storage[0].ownerType, pname).IsSubclassOf(typeof(DataObject));
                if (propertyIsMaster)
                {
                    prop.MastersTypes = new System.Type[curSource.storage.Length][];
                }

                for (int k = 0; k < curSource.storage.Length; k++)
                {
                    System.Type ownerType = curSource.storage[k].ownerType;
                    bool propsotred = IsStoredProperty(ownerType, pname);
                    string storname = propsotred ? GetPropertyStorageName(ownerType, pname) : null;
                    prop.storage[k] = new string[] { storname };
                    if (propertyIsMaster)
                    {
                        System.Type[] masterTypes = TypeUsageProvider.TypeUsage.GetUsageTypes(ownerType, pname);
                        prop.storage[k] = new string[masterTypes.Length];
                        prop.MastersTypes[k] = masterTypes;
                        if (storname != string.Empty)
                        {
                            for (int m = 0; m < masterTypes.Length; m++)
                            {
                                prop.storage[k][m] = storname + "_m" + m.ToString();
                            }
                        }
                        else
                        {
                            for (int m = 0; m < masterTypes.Length; m++)
                            {
                                prop.storage[k][m] = GetPropertyStorageName(ownerType, pname, m);
                            }
                        }

                        prop.MastersTypesCount += prop.MastersTypes[k].Length;
                    }
                }

                prop.Name = curprop.Name;
                prop.simpleName = propname[propname.Length - 1];
                prop.Stored = IsStoredProperty(curSource.storage[0].ownerType, pname);

                // if (!prop.Stored)
                {
                    prop.Expression = (string)GetExpressionForProperty(curSource.storage[0].ownerType, pname).GetMostCompatible(DataServiceType);
                }
            }

            retVal.props = props.ToArray();

            // строим структуру
            return retVal;
        }

        private static Business.StorageStructForView.PropSource AddNewSourceForHierarch(
            Business.StorageStructForView.PropSource curSource, string alias, Type propDefineType, string objectLink, bool HierLink)
        {
            var linkedSources = new ArrayList();
            linkedSources.AddRange(curSource.LinckedStorages);
            var newSource = new ICSSoft.STORMNET.Business.StorageStructForView.PropSource();

            newSource.Name = alias;
            newSource.storage[0].Storage = GetClassStorageName(propDefineType);
            newSource.storage[0].PrimaryKeyStorageName = GetPrimaryKeyStorageName(propDefineType);
            newSource.storage[0].TypeStorageName = GetTypeStorageName(propDefineType);
            newSource.storage[0].ownerType = propDefineType;
            newSource.storage[0].objectLinkStorageName = objectLink;
            newSource.HierarchicalLink = HierLink;
            newSource.ObjectLink = objectLink;

            linkedSources.Add(newSource);
            curSource.LinckedStorages = (Business.StorageStructForView.PropSource[])linkedSources.ToArray(typeof(Business.StorageStructForView.PropSource));
            return newSource;
        }

        private static Business.StorageStructForView GetHierarchicalStorageStructForView(View view, System.Type type, GetPropertiesInExpressionDelegate getPropertiesInExpression, System.Type DataServiceType)
        {
            if (type != view.DefineClassType && !type.IsSubclassOf(view.DefineClassType))
            {
                throw new ClassIsNotSubclassOfOtherException(type, view.DefineClassType);
            }

            int sourceIndex = 0;
            var allsources = new SortedList();
            var newalias = string.Empty;

            var pvs = view.Properties;
            var retVal = new Business.StorageStructForView();

            var props = new ArrayList();
            retVal.sources.storage[0].Storage = GetClassStorageName(type);
            retVal.sources.storage[0].PrimaryKeyStorageName = GetPrimaryKeyStorageName(type);
            retVal.sources.storage[0].TypeStorageName = GetTypeStorageName(type);
            retVal.sources.storage[0].ownerType = type;
            retVal.sources.ObjectLink = string.Empty;

            newalias = "A" + (sourceIndex++).ToString();
            allsources.Add("(" + view.DefineClassType.FullName + ")", retVal.sources);
            retVal.sources.Name = newalias;

            StringCollection addedProperties = new StringCollection();

            for (int i = 0; i < pvs.Length; i++)
            {
                Business.StorageStructForView.PropStorage prop = new Business.StorageStructForView.PropStorage();
                props.Add(prop);
                string spropname = pvs[i].Name;
                if (!addedProperties.Contains(spropname))
                {
                    addedProperties.Add(spropname);
                    prop.MultipleProp = false;
                }
                else
                {
                    prop.MultipleProp = true;
                }

                if (GetPropertyType(view.DefineClassType, spropname).IsSubclassOf(typeof(DataObject)))
                {
                    spropname = spropname + ".__PrimaryKey";
                }

                string scurpropnamepart = string.Empty;
                string scrupropnamepref = string.Empty;
                string[] propname = spropname.Split('.');
                string propalias = string.Empty;
                Type propType = null;
                Type propDefineType = null;
                System.Type p = type;
                Business.StorageStructForView.PropSource curSource = retVal.sources;

                for (int j = 0; j < propname.Length; j++)
                {
                    if (j == 0)
                    {
                        scurpropnamepart = propname[0];
                        scrupropnamepref = string.Empty;
                    }
                    else
                    {
                        scrupropnamepref = scurpropnamepart;
                        scurpropnamepart = scurpropnamepart + "." + propname[j];
                    }

                    propDefineType = GetPropertyDefineClassType(p, propname[j]);
                    if (propDefineType == typeof(DataObject))
                    {
                        propDefineType = p;
                    }

                    propalias = GetPropertyStorageName(p, propname[j]);

                    if (propDefineType != p)
                    {
                        string typepath = scrupropnamepref + "(" + propDefineType.FullName + ")";
                        if (!allsources.ContainsKey(typepath))
                        {
                            curSource = AddNewSourceForHierarch(curSource, "A" + (sourceIndex++).ToString(), propDefineType,
                                GetPrimaryKeyStorageName(p), true);
                            allsources.Add(typepath, curSource);
                        }
                        else
                        {
                            curSource = (Business.StorageStructForView.PropSource)allsources[typepath];
                        }
                    }

                    if (j == propname.Length - 1)
                    {
                        propType = GetPropertyType(p, propname[j]);
                    }
                    else
                    {
                        p = GetPropertyType(p, propname[j], scrupropnamepref, view.MasterTypeFilters);
                        string typepath = ((scrupropnamepref == string.Empty) ? string.Empty : (scrupropnamepref + ".")) + propname[j] + "(" + p.FullName + ")";
                        if (!allsources.Contains(typepath))
                        {
                            curSource = AddNewSourceForHierarch(
                                curSource, "A" + (sourceIndex++).ToString(),
                                p,
                                propname[j], false);
                            allsources.Add(typepath, curSource);
                        }
                        else
                        {
                            curSource = (Business.StorageStructForView.PropSource)allsources[typepath];
                        }
                    }
                }

                prop.source = curSource;
                prop.storage = new string[curSource.storage.Length][];
                prop.propertyType = propType;
                string pname = propname[propname.Length - 1];

                System.Type ownerType = curSource.storage[0].ownerType;
                string storname = GetPropertyStorageName(ownerType, pname);
                prop.storage[0] = new string[] { storname };

                prop.Name = pvs[i].Name;
                prop.simpleName = propname[propname.Length - 1];
                prop.Stored = IsStoredProperty(curSource.storage[0].ownerType, pname);

                // if (!prop.Stored)
                {
                    prop.Expression = (string)GetExpressionForProperty(curSource.storage[0].ownerType, pname).GetMostCompatible(DataServiceType);
                }
            }

            retVal.props = (Business.StorageStructForView.PropStorage[])props.ToArray(typeof(Business.StorageStructForView.PropStorage));

            // строим структуру
            return retVal;
        }

        private static TypeAtrValueCollection cacheClassStorageName = new TypeAtrValueCollection();

        /// <summary>
        /// Делегат для смены ClassStorageName (можно подставить имя_базы.dbo.имя_таблицы, например).
        /// </summary>
        /// <param name="classType">Тип класса.</param>
        /// <param name="originalStorageName">Оригинальный StorageName.</param>
        /// <returns>новый StorageName (если пустое или null, то возьмём оригинальное).</returns>
        public delegate string ChangeClassStorageNameDelegate(Type classType, string originalStorageName);

        /// <summary>
        /// Делегат для смены ClassStorageName (можно подставить имя_базы.dbo.имя_таблицы, например).
        /// </summary>
        public static ChangeClassStorageNameDelegate ChangeClassStorageName = null;

        /// <summary>
        /// Получить имя хранения для .Net-типа класса объекта данных, заданное атрибутом <see cref="ClassStorageAttribute"/>.
        /// </summary>
        /// <param name="type">.Net-тип класса объекта данных.</param>
        /// <returns>имя хранения в строке.</returns>
        public static string GetClassStorageName(System.Type type)
        {
            lock (cacheClassStorageName)
            {
                object res = cacheClassStorageName[type];
                if (res != null)
                {
                    return (string)res;
                }

                string resstr;
                object[] typeAttributes = type.GetCustomAttributes(typeof(ClassStorageAttribute), true);
                if (typeAttributes.Length == 0)
                {
                    resstr = type.Name;
                }
                else
                {
                    resstr = ((ClassStorageAttribute)typeAttributes[0]).Name;
                }

                // обработаем делегат
                if (ChangeClassStorageName != null)
                {
                    string changedClassStorageName = ChangeClassStorageName(type, resstr);
                    if (!string.IsNullOrEmpty(changedClassStorageName))
                    {
                        resstr = changedClassStorageName;
                    }
                }

                cacheClassStorageName[type] = resstr;
                return resstr;
            }
        }

        /// <summary>
        /// Clear cache for <see cref="GetClassStorageName(Type)"/> method.
        /// </summary>
        public static void ClearGetClassStorageName()
        {
            lock (cacheClassStorageName)
            {
                cacheClassStorageName = new TypeAtrValueCollection();
            }
        }

        private static TypeAtrValueCollection cacheAutoAlteredClass = new TypeAtrValueCollection();

        /// <summary>
        /// Является ли класс AutoAltered.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public static bool AutoAlteredClass(System.Type type)
        {
            lock (cacheAutoAlteredClass)
            {
                object res = cacheAutoAlteredClass[type];
                if (res != null)
                {
                    return (bool)res;
                }
                else
                {
                    bool bres;
                    object[] typeAttributes = type.GetCustomAttributes(typeof(AutoAlteredAttribute), true);
                    if (typeAttributes.Length == 0)
                    {
                        bres = false;
                    }
                    else
                    {
                        bres = ((AutoAlteredAttribute)typeAttributes[0]).value;
                    }

                    cacheAutoAlteredClass[type] = bres;
                    return bres;
                }
            }
        }

        private static TypeAtrValueCollection cacheAssemblyStorageName = new TypeAtrValueCollection();

        /// <summary>
        /// Получить имя хранения для сборки, заданное атрибутом <see cref="AssemblyStorageAttribute"/>.
        /// </summary>
        /// <param name="type">.Net-тип сборки.</param>
        /// <returns>имя хранения.</returns>
        public static string GetAssemblyStorageName(System.Type type)
        {
            lock (cacheAssemblyStorageName)
            {
                object res = cacheAssemblyStorageName[type];
                if (res != null)
                {
                    return (string)res;
                }
                else
                {
                    string sres;
                    object[] typeAttributes = type.Assembly.GetCustomAttributes(typeof(AssemblyStorageAttribute), true);
                    if (typeAttributes.Length == 0)
                    {
                        sres = string.Empty;
                    }
                    else
                    {
                        sres = ((AssemblyStorageAttribute)typeAttributes[0]).Name;
                    }

                    cacheAssemblyStorageName[type] = sres;
                    return sres;
                }
            }
        }

        private static TypePropertyAtrValueCollection cacheSortByLoadingOrder = new TypePropertyAtrValueCollection();

        /// <summary>
        /// Отсортировать, согласно LoadingOrder для указанного класса.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="props"></param>
        /// <returns></returns>
        public static string[] SortByLoadingOrder(System.Type type, string[] props)
        {
            lock (cacheSortByLoadingOrder)
            {
                string key = string.Join(",", props);
                string[] res = (string[])cacheSortByLoadingOrder[type, key];
                if (res != null)
                {
                    return CopyStringArray(res);
                }
                else
                {
                    string[] ls = GetLoadingOrder(type);
                    Array.Sort(props);
                    res = new string[props.Length];
                    int curindex = 0;
                    for (int i = 0; i < ls.Length; i++)
                    {
                        int index = Array.BinarySearch(props, ls[i]);
                        if (index >= 0)
                        {
                            res[curindex++] = ls[i];
                            props[index] = string.Empty;
                        }
                    }

                    for (int i = 0; i < props.Length; i++)
                    {
                        if (props[i] != string.Empty)
                        {
                            res[curindex++] = props[i];
                        }
                    }

                    cacheSortByLoadingOrder[type, key] = res;
                    return CopyStringArray(res);
                }
            }
        }

        /// <summary>
        /// Используйте метод GetAlteredPropertyNames.
        /// </summary>
        /// <param name="obj1"></param>
        /// <param name="obj2"></param>
        /// <param name="withDetailsComparing"></param>
        /// <returns></returns>
        [Obsolete]
        public static string[] GetAlteredProperyNames(DataObject obj1, DataObject obj2, bool withDetailsComparing)
        {
            return GetAlteredPropertyNames(obj1, obj2, withDetailsComparing);
        }

        /// <summary>
        /// Сравнить два объекта данных и вернуть список различающихся .Net-свойств. (Объект или свойство с атрибутом NotStored проверяться не будет).
        /// </summary>
        /// <param name="obj1">1-й объект данных.</param>
        /// <param name="obj2">2-й объект данных.</param>
        /// <param name="WithDetailsComparing">со сравниванием детейловах объектов.</param>
        /// <returns>одномерный строковый массив имён свойств.</returns>
        public static string[] GetAlteredPropertyNames(DataObject obj1, DataObject obj2, bool WithDetailsComparing)
        {
            if (obj1 == null && obj2 == null)
            {
                return new string[0];
            }
            else
            {
                if (obj1 == null || obj2 == null)
                {
                    if (obj1 != null)
                    {
                        return GetStorablePropertyNames(obj1.GetType());
                    }
                    else
                    {
                        return GetStorablePropertyNames(obj2.GetType());
                    }
                }
                else
                {
                    if (obj1.GetType() != obj2.GetType())
                    {
                        throw new DifferentDataObjectTypesException();
                    }
                    else
                    {
                        System.Type type = obj1.GetType();
                        string[] props = GetStorablePropertyNames(type);
                        var Arr = new ArrayList(props.Length);
                        System.Type dobjectType = typeof(DataObject);
                        System.Type darrayType = typeof(DetailArray);
                        for (int i = 0; i < props.Length; i++)
                        {
                            System.Type propType = GetPropertyType(type, props[i]);

                            object val1 = GetPropValueByName(obj1, props[i]);
                            object val2 = GetPropValueByName(obj2, props[i]);
                            bool UnAltered = false;
                            if (val1 == null && val2 == null)
                            {
                                UnAltered = true;
                            }
                            else if (val1 == null || val2 == null)
                            {
                                UnAltered = false;
                            }
                            else if (propType.IsValueType)
                            {
                                UnAltered = val1.Equals(val2);
                            }
                            else if (propType.IsSubclassOf(dobjectType))
                            {
                                UnAltered = val1.GetType() == val2.GetType() &&
                                    ((DataObject)val1).__PrimaryKey.Equals(((DataObject)val2).__PrimaryKey);
                            }
                            else if (propType.IsSubclassOf(darrayType))
                            {
                                DetailArray ar1 = (DetailArray)val1;
                                DetailArray ar2 = (DetailArray)val2;
                                if (ar1.Count != ar2.Count)
                                {
                                    UnAltered = false;
                                }
                                else
                                {
                                    UnAltered = true;
                                    for (int j = 0; j < ar1.Count; j++)
                                    {
                                        DataObject do1 = ar1.ItemByIndex(j);
                                        DataObject do2 = ar2.GetByKey(do1.__PrimaryKey); // ar2.ItemByIndex(j);

                                        if (do2 == null)
                                        {
                                            UnAltered = false;
                                            break;
                                        }

                                        UnAltered = do1.GetType() == do2.GetType() && do1.GetStatus() == do2.GetStatus() && do1.__PrimaryKey.Equals(do2.__PrimaryKey);
                                        if (UnAltered && WithDetailsComparing)
                                        {
                                            UnAltered = GetAlteredPropertyNames(do1, do2, true).Length == 0;
                                        }

                                        if (!UnAltered)
                                        {
                                            break;
                                        }
                                    }
                                }
                            }
                            else if (val1 is Geography && val2 is Geography)
                            {
                                UnAltered = ((Geography)val1).Equals((Geography)val2);
                            }
                            else if (val1 is Geometry && val2 is Geometry)
                            {
                                UnAltered = ((Geometry)val1).Equals((Geometry)val2);
                            }
                            else if (val1 is IComparableType)
                            {
                                UnAltered = ((IComparableType)val1).Compare(val2) == 0;
                            }
                            else
                            {
                                UnAltered = val1.ToString() == val2.ToString();
                            }

                            if (!UnAltered)
                            {
                                Arr.Add(props[i]);
                            }
                        }

                        string[] retval = new string[Arr.Count];
                        Arr.CopyTo(retval);
                        Arr.Clear();
                        return retval;
                    }
                }
            }
        }

        /// <summary>
        /// Сравнить два объекта данных и вернуть список различающихся .Net-свойств. (NotStored-атрибуты не игнорируются и тоже проверяются вместе с остальными).
        /// </summary>
        /// <param name="obj1">1-й объект данных.</param>
        /// <param name="obj2">2-й объект данных.</param>
        /// <param name="WithDetailsComparing">со сравниванием детейловах объектов.</param>
        /// <returns>одномерный строковый массив имён свойств.</returns>
        public static string[] GetAlteredPropertyNamesWithNotStored(DataObject obj1, DataObject obj2, bool WithDetailsComparing)
        {
            if (obj1 == null && obj2 == null)
            {
                return new string[0];
            }

            if (obj1 == null || obj2 == null)
            {
                if (obj1 != null)
                {
                    return GetAllPropertyNames(obj1.GetType());
                }

                return GetAllPropertyNames(obj2.GetType());
            }

            if (obj1.GetType() != obj2.GetType())
            {
                throw new DifferentDataObjectTypesException();
            }

            var type = obj1.GetType();
            string[] props = GetAllPropertyNames(type);
            var Arr = new ArrayList(props.Length);
            Type dobjectType = typeof(DataObject);
            Type darrayType = typeof(DetailArray);
            for (int i = 0; i < props.Length; i++)
            {
                Type propType = GetPropertyType(type, props[i]);

                object val1 = GetPropValueByName(obj1, props[i]);
                object val2 = GetPropValueByName(obj2, props[i]);
                bool UnAltered = false;
                if (val1 == null && val2 == null)
                {
                    UnAltered = true;
                }
                else if (val1 == null || val2 == null)
                {
                    UnAltered = false;
                }
                else if (propType.IsValueType)
                {
                    UnAltered = val1.Equals(val2);
                }
                else if (propType.IsSubclassOf(dobjectType))
                {
                    UnAltered = val1.GetType() == val2.GetType() &&
                                 ((DataObject)val1).__PrimaryKey.Equals(((DataObject)val2).__PrimaryKey);
                }
                else if (propType.IsSubclassOf(darrayType))
                {
                    DetailArray ar1 = (DetailArray)val1;
                    DetailArray ar2 = (DetailArray)val2;
                    if (ar1.Count != ar2.Count)
                    {
                        UnAltered = false;
                    }
                    else
                    {
                        UnAltered = true;
                        for (int j = 0; j < ar1.Count; j++)
                        {
                            DataObject do1 = ar1.ItemByIndex(j);
                            DataObject do2 = ar2.GetByKey(do1.__PrimaryKey); // ar2.ItemByIndex(j);

                            if (do2 == null)
                            {
                                UnAltered = false;
                                break;
                            }

                            UnAltered = do1.GetType() == do2.GetType() && do1.GetStatus() == do2.GetStatus() && do1.__PrimaryKey.Equals(do2.__PrimaryKey);
                            if (UnAltered && WithDetailsComparing)
                            {
                                UnAltered = GetAlteredPropertyNames(do1, do2, true).Length == 0;
                            }

                            if (!UnAltered)
                            {
                                break;
                            }
                        }
                    }
                }
                else if (val1 is Geography && val2 is Geography)
                {
                    UnAltered = ((Geography)val1).Equals((Geography)val2);
                }
                else if (val1 is Geometry && val2 is Geometry)
                {
                    UnAltered = ((Geometry)val1).Equals((Geometry)val2);
                }
                else if (val1 is IComparableType)
                {
                    UnAltered = ((IComparableType)val1).Compare(val2) == 0;
                }
                else
                {
                    UnAltered = val1.ToString() == val2.ToString();
                }

                if (!UnAltered)
                {
                    Arr.Add(props[i]);
                }
            }

            string[] retval = new string[Arr.Count];
            Arr.CopyTo(retval);
            Arr.Clear();
            return retval;
        }

        /// <summary>
        /// Сравнить два объекта данных и вернуть true - если объекты различаются.
        /// </summary>
        /// <param name="obj1">1-й объект данных.</param>
        /// <param name="obj2">2-й объект данных.</param>
        /// <param name="WithDetailsComparing">со сравниванием детейловах объектов.</param>
        /// <returns>одномерный строковый массив имён свойств.</returns>
        public static bool ContainsAlteredProps(DataObject obj1, DataObject obj2, bool WithDetailsComparing)
        {
            if (obj1 == null && obj2 == null)
            {
                return false;
            }
            else
            {
                if (obj1 == null || obj2 == null)
                {
                    return true;
                }
                else
                {
                    if (obj1.GetType() != obj2.GetType())
                    {
                        throw new DifferentDataObjectTypesException();
                    }
                    else
                    {
                        System.Type type = obj1.GetType();
                        string[] props = GetStorablePropertyNames(type);

                        // ArrayList Arr = new ArrayList(props.Length);
                        System.Type dobjectType = typeof(DataObject);
                        System.Type darrayType = typeof(DetailArray);
                        for (int i = 0; i < props.Length; i++)
                        {
                            System.Type propType = GetPropertyType(type, props[i]);

                            object val1 = GetPropValueByName(obj1, props[i]);
                            object val2 = GetPropValueByName(obj2, props[i]);
                            bool UnAltered = false;
                            if (val1 == null && val2 == null)
                            {
                                UnAltered = true;
                            }
                            else if (val1 == null || val2 == null)
                            {
                                // UnAltered = false;
                                return true;
                            }
                            else if (propType.IsValueType)
                            {
                                UnAltered = val1.Equals(val2);
                                if (!UnAltered)
                                {
                                    return true;
                                }
                            }
                            else if (propType.IsSubclassOf(dobjectType))
                            {
                                UnAltered = val1.GetType() == val2.GetType() &&
                                    ((DataObject)val1).__PrimaryKey.Equals(((DataObject)val2).__PrimaryKey);
                                if (!UnAltered)
                                {
                                    return true;
                                }
                            }
                            else if (propType.IsSubclassOf(darrayType))
                            {
                                DetailArray ar1 = (DetailArray)val1;
                                DetailArray ar2 = (DetailArray)val2;
                                if (ar1.Count != ar2.Count)
                                {
                                    // UnAltered = false;
                                    return true;
                                }
                                else
                                {
                                    UnAltered = true;
                                    for (int j = 0; j < ar1.Count; j++)
                                    {
                                        DataObject do1 = ar1.ItemByIndex(j);
                                        DataObject do2 = ar2.GetByKey(do1.__PrimaryKey); // ar2.ItemByIndex(j);

                                        if (do2 == null)
                                        {
                                            // UnAltered = false;
                                            // break;
                                            return true;
                                        }

                                        UnAltered = do1.GetType() == do2.GetType() && do1.GetStatus() == do2.GetStatus() && do1.__PrimaryKey.Equals(do2.__PrimaryKey);
                                        if (UnAltered && WithDetailsComparing)
                                        {
                                            UnAltered = !ContainsAlteredProps(do1, do2, true);
                                        }

                                        // UnAltered = GetAlteredProperyNames(do1, do2, true).Length == 0;
                                        if (!UnAltered)
                                        {
                                            return true;
                                        }
                                    }
                                }
                            }
                            else if (val1 is Geography && val2 is Geography)
                            {
                                UnAltered = ((Geography)val1).Equals((Geography)val2);
                            }
                            else if (val1 is Geometry && val2 is Geometry)
                            {
                                UnAltered = ((Geometry)val1).Equals((Geometry)val2);
                            }
                            else if (val1 is IComparableType)
                            {
                                UnAltered = ((IComparableType)val1).Compare(val2) == 0;
                            }
                            else
                            {
                                UnAltered = val1.ToString() == val2.ToString();
                            }

                            if (!UnAltered)
                            {
                                return true;
                            }
                        }

                        return false;
                    }
                }
            }
        }

        private static ConcurrentDictionary<Type, List<string>> cacheAllPropertyNames = new ConcurrentDictionary<Type, List<string>>();

        /// <summary>
        /// Вернуть все имена .Net-свойств для .Net-типа класса объекта данных.
        /// </summary>
        /// <param name="type">.Net-тип класса объекта данных.</param>
        /// <returns>одномерный строковый массив имён свойств.</returns>
        public static string[] GetAllPropertyNames(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            return cacheAllPropertyNames
                .GetOrAdd(
                    type,
                    t => type.GetProperties().Select(p => p.Name).ToList())
                .ToArray();
        }

        /// <summary>
        /// Проверить есть ли такое свойство в указанном типе.
        /// </summary>
        /// <param name="type">.Net-тип класса объекта данных.</param>
        /// <param name="propName">Имя свойства.</param>
        /// <returns>true - свойство есть, false - нет.</returns>
        public static bool CheckPropertyExist(Type type, string propName)
        {
            if (type == null)
            {
                throw new Exception("Не указан тип для определения наличия свойства <" + propName + "> в нём");
            }

            return GetAllPropertyNames(type).Contains(propName);
        }

        private static TypeAtrValueCollection cacheAutoStoreMastersDisabled = new TypeAtrValueCollection();

        /// <summary>
        /// Вернуть имена .Net-свойств для .Net-типа класса объекта данных, мастеровых,
        /// для которых отключено автосохранение атрибутом <see cref="AutoStoreMasterDisabled"/>.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public static string[] GetAutoStoreMastersDisabled(System.Type type)
        {
            lock (cacheAutoStoreMastersDisabled)
            {
                var res = (string[])cacheAutoStoreMastersDisabled[type];
                if (res != null)
                {
                    return CopyStringArray(res);
                }
                else
                {
                    var Properties = type.GetProperties();
                    var RetArray = new ArrayList();

                    for (int i = 0; i < Properties.Length; i++)
                    {
                        if (Properties[i].PropertyType.IsSubclassOf(typeof(DataObject)))
                        {
                            object[] myAttributes = Properties[i].GetCustomAttributes(typeof(AutoStoreMasterDisabled), true);
                            if (myAttributes.Length > 0)
                            {
                                AutoStoreMasterDisabled autostoremaster = (AutoStoreMasterDisabled)myAttributes[0];
                                if (autostoremaster.AutoCreateMasterDisabled)
                                {
                                    RetArray.Add(Properties[i].Name);
                                }
                            }
                        }
                    }

                    var returnValue = new string[RetArray.Count];
                    RetArray.CopyTo(returnValue);
                    RetArray.Clear();
                    RetArray = null;

                    // Для генерённых на ходу типов не добавляем в кеш, т.к. они меняются в любой момент (например редактор параметров генерит фиктивный тип для задания параметров и формы параметров)
                    if (type.Assembly.FullName != "TempAssembly, Version=0.0.0.0")
                    {
                        cacheAutoStoreMastersDisabled[type] = returnValue;
                    }

                    return CopyStringArray(returnValue);
                }
            }
        }

        private static TypeAtrValueCollection cacheStorablePropertyNames = new TypeAtrValueCollection();

        /// <summary>
        /// Вернуть имена .Net-свойств для .Net-типа класса объекта данных,
        /// которые хранятся (не содержат атрибут <see cref="NotStoredAttribute"/>).
        /// </summary>
        /// <param name="type">.Net-тип класса объекта данных.</param>
        /// <returns>одномерный строковый массив имён свойств.</returns>
        public static string[] GetStorablePropertyNames(System.Type type)
        {
            lock (cacheStorablePropertyNames)
            {
                string[] res = (string[])cacheStorablePropertyNames[type];
                if (res != null)
                {
                    return CopyStringArray(res);
                }
                else
                {
                    var Properties = type.GetProperties();
                    var RetArray = new ArrayList();
                    object[] typeAttributes = type.GetCustomAttributes(typeof(NotStoredAttribute), false);
                    if (typeAttributes.Length == 0 || ((NotStoredAttribute)typeAttributes[0]).Value == false)
                    {
                        for (int i = 0; i < Properties.Length; i++)
                        {
                            object[] myAttributes = Properties[i].GetCustomAttributes(typeof(NotStoredAttribute), true);
                            if (myAttributes.Length > 0)
                            {
                                NotStoredAttribute notStored = (NotStoredAttribute)myAttributes[0];
                                if (!notStored.Value)
                                {
                                    RetArray.Add(Properties[i].Name);
                                }
                            }
                            else
                            {
                                RetArray.Add(Properties[i].Name);
                            }
                        }
                    }

                    var returnValue = new string[RetArray.Count];
                    RetArray.CopyTo(returnValue);
                    RetArray.Clear();
                    RetArray = null;

                    // Для генерённых на ходу типов не добавляем в кеш, т.к. они меняются в любой момент (например редактор параметров генерит фиктивный тип для задания параметров и формы параметров)
                    if (type.Assembly.FullName != "TempAssembly, Version=0.0.0.0")
                    {
                        cacheStorablePropertyNames[type] = returnValue;
                    }

                    return CopyStringArray(returnValue);
                }
            }
        }

        private static TypeAtrValueCollection cachePropertyNamesForInsert = new TypeAtrValueCollection();

        /// <summary>
        /// Получить все свойства объекта, которые являются хранимыми и требуются при создании экземпляра объекта в БД.
        /// </summary>
        /// <param name="type">.Net-тип класса объекта данных.</param>
        /// <returns>Одномерный строковый массив имён свойств.</returns>
        public static string[] GetPropertyNamesForInsert(System.Type type)
        {
            lock (cachePropertyNamesForInsert)
            {
                string[] res = (string[])cachePropertyNamesForInsert[type];
                if (res != null)
                {
                    return CopyStringArray(res);
                }
                else
                {
                    PropertyInfo[] properties = type.GetProperties();
                    var retArray = new ArrayList();
                    object[] typeAttributes = type.GetCustomAttributes(typeof(NotStoredAttribute), false);
                    if (typeAttributes.Length == 0 || ((NotStoredAttribute)typeAttributes[0]).Value == false)
                    {
                        for (int i = 0; i < properties.Length; i++)
                        {
                            PropertyInfo propertyInfo = properties[i];
                            string name = propertyInfo.Name;
                            bool needAddProp = false;
                            object[] notStoredAttributes = propertyInfo.GetCustomAttributes(typeof(NotStoredAttribute), true);
                            if (notStoredAttributes.Length > 0)
                            {
                                NotStoredAttribute notStored = (NotStoredAttribute)notStoredAttributes[0];
                                if (!notStored.Value)
                                {
                                    needAddProp = true;
                                }
                            }
                            else
                            {
                                needAddProp = true;
                            }

                            if (needAddProp)
                            {
                                object[] disableInsertPropertyAttributes =
                                    propertyInfo.GetCustomAttributes(typeof(DisableInsertPropertyAttribute), true);
                                if (disableInsertPropertyAttributes.Length > 0)
                                {
                                    DisableInsertPropertyAttribute disableInsertProperty =
                                        (DisableInsertPropertyAttribute)disableInsertPropertyAttributes[0];
                                    if (disableInsertProperty.Value)
                                    {
                                        needAddProp = false;
                                    }
                                }
                            }

                            if (needAddProp)
                            {
                                retArray.Add(name);
                            }
                        }
                    }

                    string[] returnValue = new string[retArray.Count];
                    retArray.CopyTo(returnValue);
                    retArray.Clear();
                    retArray = null;

                    // Для генерённых на ходу типов не добавляем в кеш, т.к. они меняются в любой момент (например редактор параметров генерит фиктивный тип для задания параметров и формы параметров)
                    if (type.Assembly.FullName != "TempAssembly, Version=0.0.0.0")
                    {
                        cachePropertyNamesForInsert[type] = returnValue;
                    }

                    return CopyStringArray(returnValue);
                }
            }
        }

        private static string[] CopyStringArray(string[] a)
        {
            if (a == null)
            {
                return null;
            }
            else
            {
                return (string[])a.Clone();
            }
        }

        private static Type[] CopyTypeArray(Type[] a)
        {
            if (a == null)
            {
                return null;
            }
            else
            {
                return (Type[])a.Clone();
            }
        }

        private static TypeAtrValueCollection cacheGetNotStorablePropertyNames = new TypeAtrValueCollection();

        /// <summary>
        /// Вернуть имена .Net-свойств для .Net-типа класса объекта данных,
        /// которые не хранятся (управление атрибутом <see cref="NotStoredAttribute"/>).
        /// </summary>
        /// <param name="type">.Net-тип класса объекта данных.</param>
        /// <returns>одномерный строковый массив имён свойств.</returns>
        public static string[] GetNotStorablePropertyNames(System.Type type)
        {
            lock (cacheGetNotStorablePropertyNames)
            {
                string[] res = (string[])cacheGetNotStorablePropertyNames[type];
                if (res != null)
                {
                    return CopyStringArray(res);
                }
                else
                {
                    PropertyInfo[] Properties = type.GetProperties();
                    ArrayList RetArray = new ArrayList(Properties.Length);
                    for (int i = 0; i < Properties.Length; i++)
                    {
                        object[] myAttributes = Properties[i].GetCustomAttributes(typeof(NotStoredAttribute), true);
                        if (myAttributes.Length > 0)
                        {
                            NotStoredAttribute notStored = (NotStoredAttribute)myAttributes[0];
                            if (notStored.Value)
                            {
                                RetArray.Add(Properties[i].Name);
                            }
                        }
                    }

                    string[] returnValue = new string[RetArray.Count];
                    RetArray.CopyTo(returnValue);
                    RetArray.Clear();
                    RetArray = null;
                    cacheGetNotStorablePropertyNames[type] = returnValue;
                    return returnValue;
                }
            }
        }

        private static ConcurrentDictionary<long, bool> cacheIsStoredProp = new ConcurrentDictionary<long, bool>();

        /// <summary>
        /// Хранимое ли свойство.
        /// </summary>
        /// <param name="type">тип объекта данных.</param>
        /// <param name="propName">свойство.</param>
        /// <returns></returns>
        public static bool IsStoredProperty(Type type, string propName)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (propName == null)
            {
                throw new ArgumentNullException(nameof(propName));
            }

            long key = (type.GetHashCode() * 10000000000) + propName.GetHashCode();

            return cacheIsStoredProp.GetOrAdd(key, k => IsStoredPropertyInternal(type, propName));
        }

        private static bool IsStoredPropertyInternal(Type type, string propName)
        {
            bool bres;

            int pointIndex = propName.IndexOf(".", StringComparison.InvariantCultureIgnoreCase);
            if (pointIndex >= 0)
            {
                string masterName = propName.Substring(0, pointIndex);
                string masterPropName = propName.Substring(pointIndex + 1);
                bres = IsStoredPropertyInternal(GetPropertyType(type, masterName), masterPropName);
            }
            else
            {
                PropertyInfo prop = type.GetProperty(propName);
                if (prop == null)
                {
                    throw new NoSuchPropertyException(type, propName);
                }

                var myAttributes = prop.GetCustomAttributes(typeof(NotStoredAttribute), true);
                if (myAttributes.Length > 0)
                {
                    var notStored = (NotStoredAttribute)myAttributes[0];
                    bres = !notStored.Value;
                }
                else
                {
                    bres = true;
                }
            }

            return bres;
        }

        private static TypeAtrValueCollection cacheIsStoredType = new TypeAtrValueCollection();

        /// <summary>
        /// Хранимый ли класс.
        /// </summary>
        /// <param name="type">тип объекта данных.</param>
        /// <returns></returns>
        public static bool IsStoredType(Type type)
        {
            lock (cacheIsStoredType)
            {
                object res = cacheIsStoredType[type];
                if (res != null)
                {
                    return (bool)res;
                }

                bool bres;

                object[] myAttributes = type.GetCustomAttributes(typeof(NotStoredAttribute), false);
                if (myAttributes.Length > 0)
                {
                    var notStored = (NotStoredAttribute)myAttributes[0];
                    bres = !notStored.Value;
                }
                else
                {
                    bres = true;
                }

                cacheIsStoredType[type] = bres;
                return bres;
            }
        }

        private static TypePropertyAtrValueCollection cacheCanWriteProperty = new TypePropertyAtrValueCollection();

        /// <summary>
        /// Можно ли писать в это свойство.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="propName"></param>
        /// <returns></returns>
        public static bool CanWriteProperty(System.Type type, string propName)
        {
            lock (cacheCanWriteProperty)
            {
                object res = cacheCanWriteProperty[type, propName];
                if (res != null)
                {
                    return (bool)res;
                }
                else
                {
                    int pointIndex = propName.IndexOf(".");
                    if (pointIndex >= 0)
                    {
                        string MasterName = propName.Substring(0, pointIndex);
                        string MasterPropName = propName.Substring(pointIndex + 1);
                        res = CanWriteProperty(GetPropertyType(type, MasterName), MasterPropName);
                        cacheCanWriteProperty[type, propName] = res;
                        return (bool)res;
                    }
                    else
                    {
                        PropertyInfo prop = type.GetProperty(propName);
                        cacheCanWriteProperty[type, propName] = prop.CanWrite;
                        cacheCanReadProperty[type, propName] = prop.CanRead;
                        return prop.CanWrite;
                    }
                }
            }
        }

        private static TypePropertyAtrValueCollection cacheCanReadProperty = new TypePropertyAtrValueCollection();

        /// <summary>
        /// Можно ли читать из этого свойства.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="propName"></param>
        /// <returns></returns>
        public static bool CanReadProperty(System.Type type, string propName)
        {
            lock (cacheCanReadProperty)
            {
                object res = cacheCanReadProperty[type, propName];
                if (res != null)
                {
                    return (bool)res;
                }
                else
                {
                    int pointIndex = propName.IndexOf(".");
                    if (pointIndex >= 0)
                    {
                        string MasterName = propName.Substring(0, pointIndex);
                        string MasterPropName = propName.Substring(pointIndex + 1);
                        res = CanReadProperty(GetPropertyType(type, MasterName), MasterPropName);
                        cacheCanReadProperty[type, propName] = res;
                        return (bool)res;
                    }
                    else
                    {
                        PropertyInfo prop = type.GetProperty(propName);
                        cacheCanWriteProperty[type, propName] = prop.CanWrite;
                        cacheCanReadProperty[type, propName] = prop.CanRead;
                        return prop.CanRead;
                    }
                }
            }
        }

        private static TypePropertyAtrValueCollection cacheGetPropertyNamesByType = new TypePropertyAtrValueCollection();

        /// <summary>
        /// Возвращает список свойств указанного шаблонного типа для .Net-класса объекта данных.
        /// </summary>
        /// <param name="typeofDataObject">.Net-тип класса объекта данных.</param>
        /// <param name="templatetype">шаблонный тип свойства.</param>
        /// <returns>одномерный строковый массив имён свойств.</returns>
        public static string[] GetPropertyNamesByType(System.Type typeofDataObject, System.Type templatetype)
        {
            lock (cacheGetPropertyNamesByType)
            {
                string key = templatetype.Name;
                string[] res = (string[])cacheGetPropertyNamesByType[typeofDataObject, key];
                if (res != null)
                {
                    return CopyStringArray(res);
                }
                else
                {
                    PropertyInfo[] Properties = typeofDataObject.GetProperties();
                    ArrayList RetArray = new ArrayList(Properties.Length);
                    for (int i = 0; i < Properties.Length; i++)
                    {
                        if (Properties[i].PropertyType.IsSubclassOf(templatetype) || Properties[i].PropertyType.Equals(templatetype))
                        {
                            RetArray.Add(Properties[i].Name);
                        }
                    }

                    string[] returnValue = new string[RetArray.Count];
                    RetArray.CopyTo(returnValue);
                    RetArray.Clear();
                    RetArray = null;

                    // Для генерённых на ходу типов не добавляем в кеш, т.к. они меняются в любой момент (например редактор параметров генерит фиктивный тип для задания параметров и формы параметров)
                    if (typeofDataObject.Assembly.FullName != "TempAssembly, Version=0.0.0.0")
                    {
                        cacheGetPropertyNamesByType[typeofDataObject, key] = returnValue;
                    }

                    return returnValue;
                }
            }
        }

        private static TypeAtrValueCollection cacheGetAgregatePropertyName = new TypeAtrValueCollection();

        /// <summary>
        /// Получить имя свойства -- шапки, указанного атрибутом <see cref="AgregatorAttribute"/>).
        /// </summary>
        /// <param name="type">.Net-тип класса объекта данных.</param>
        /// <returns>имя свойства.</returns>
        public static string GetAgregatePropertyName(System.Type type)
        {
            lock (cacheGetAgregatePropertyName)
            {
                object res = cacheGetAgregatePropertyName[type];
                if (res != null)
                {
                    return (string)res;
                }
                else
                {
                    string sres = GetPropertyName(type, typeof(AgregatorAttribute), true);
                    cacheGetAgregatePropertyName[type] = sres;
                    return sres;
                }
            }
        }

        /// <summary>
        /// Получить имя свойства, в котором хранится массив детейлов определенного типа.
        /// </summary>
        /// <param name="aggregatorType">Тип агрегатора.</param>
        /// <param name="detailType">Тип детейлов.</param>
        /// <returns>Имя свойства. В случае отсутствия в типе агрегатора детейла указанного типа, будет возвращен null.</returns>
        public static string GetDetailArrayPropertyName(Type aggregatorType, Type detailType)
        {
            if (aggregatorType == null)
            {
                throw new ArgumentNullException("type");
            }

            if (detailType == null)
            {
                throw new ArgumentNullException("detailType");
            }

            var properties = aggregatorType.GetProperties();
            foreach (var propertyInfo in properties)
            {
                if (propertyInfo.PropertyType.IsSubclassOf(typeof(DetailArray)) && GetItemType(aggregatorType, propertyInfo.Name) == detailType)
                {
                    return propertyInfo.Name;
                }
            }

            return null;
        }

        /// <summary>
        /// Получить имя свойства, у которого установлен указанный атрибут.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="attribute"></param>
        /// <param name="inherit"></param>
        /// <returns></returns>
        private static string GetPropertyName(System.Type type, System.Type attribute, bool inherit)
        {
            PropertyInfo[] Properties = type.GetProperties();
            for (int i = 0; i < Properties.Length; i++)
            {
                object[] myAttributes = Properties[i].GetCustomAttributes(attribute, inherit);
                if (myAttributes.Length > 0)
                {
                    return Properties[i].Name;
                }
            }

            if (!inherit || type == typeof(DataObject) || type == typeof(object))
            {
                return string.Empty;
            }
            else
            {
                return GetPropertyName(type.BaseType, attribute, inherit);
            }
        }

        private static TypeAtrValueCollection cacheOrderPropertyType = new TypeAtrValueCollection();

        /// <summary>
        /// Вернуть свойство, по которому нужно упорядочивать.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public static string GetOrderPropertyName(System.Type type)
        {
            lock (cacheOrderPropertyType)
            {
                object res = cacheOrderPropertyType[type];
                if (res == null)
                {
                    res = GetPropertyName(type, typeof(OrderAttribute), true);
                    cacheOrderPropertyType[type] = res;
                }

                return (string)res;
            }
        }

        private static TypePropertyAtrValueCollection cacheGetExpressionForProperty = new TypePropertyAtrValueCollection();

        /// <summary>
        /// Вернуть выражения, указанные атрибутами <see cref="DataServiceExpressionAttribute"/> для свойства.
        /// </summary>
        /// <param name="type">тип.</param>
        /// <param name="propName">свойство.</param>
        /// <returns></returns>
        public static TypeBaseCollection GetExpressionForProperty(Type type, string propName)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (propName == null)
            {
                throw new ArgumentNullException(nameof(propName));
            }

            lock (cacheGetExpressionForProperty)
            {
                TypeBaseCollection res = (TypeBaseCollection)cacheGetExpressionForProperty[type, propName];
                if (res != null)
                {
                    return res;
                }

                int pntIndex = propName.LastIndexOf('.');
                if (pntIndex >= 0)
                {
                    string masterName = propName.Substring(0, pntIndex);
                    type = GetPropertyType(type, masterName);
                    propName = propName.Substring(pntIndex + 1);
                }

                // Снова ищем в кэше по новому пути.
                res = (TypeBaseCollection)cacheGetExpressionForProperty[type, propName];
                if (res != null)
                {
                    return res;
                }

                res = new TypeBaseCollection();
                PropertyInfo prop = type.GetProperty(propName);
                if (prop != null)
                {
                    object[] myAttributes = prop.GetCustomAttributes(typeof(DataServiceExpressionAttribute), true);
                    foreach (DataServiceExpressionAttribute atr in myAttributes)
                    {
                        var key = atr.TypeofDataService;
                        if (key != null)
                        {
                            res.Add(key, atr.Expression);
                        }
                    }

                    cacheGetExpressionForProperty[type, propName] = res;
                }

                return res;
            }
        }

        /// <summary>
        /// Добавить недостающие свойства в представление из выражений нехранимых свойств.
        /// </summary>
        /// <param name="dataObjectView">Представление.</param>
        /// <param name="dsType">Тип сервиса данных.</param>
        public static void AppendPropertiesFromNotStored(View dataObjectView, Type dsType)
        {
            var notStoredProps = dataObjectView.Properties.Where(p => !IsStoredProperty(dataObjectView.DefineClassType, p.Name));
            foreach (var notStoredProp in notStoredProps)
            {
                string expression = GetPropertyExpression(dataObjectView.DefineClassType, notStoredProp.Name, dsType);
                if (!string.IsNullOrEmpty(expression))
                {
                    int lastDotIndex = notStoredProp.Name.LastIndexOf(".", StringComparison.InvariantCultureIgnoreCase);
                    string notStoredPropOwner = lastDotIndex != -1
                        ? notStoredProp.Name.Substring(0, lastDotIndex + 1)
                        : string.Empty;
                    var propertiesInExpression = GetPropertiesInExpression(expression, notStoredPropOwner);
                    dataObjectView.AddProperties(propertiesInExpression);
                }
            }
        }

        /// <summary>
        /// Вернуть выражение с учетом DataService. <see cref="DataServiceExpressionAttribute"/> для свойства.
        /// </summary>
        /// <param name="type">Тип объекта.</param>
        /// <param name="propertyName">Имя свойства.</param>
        /// <param name="dataServiceType">Тип сервиса данных.</param>
        /// <returns>Строка выражения.</returns>
        public static string GetPropertyExpression(Type type, string propertyName, Type dataServiceType)
        {
            return (string)GetExpressionForProperty(type, propertyName).GetMostCompatible(dataServiceType);
        }

        private static TypeAtrValueCollection cacheLoadingOrder = new TypeAtrValueCollection();

        /// <summary>
        /// Вернуть порядок (установленный <see cref="LoadingOrderAttribute"/>), в соответствии с которым происходит загрузка свойств объекта данных.
        /// </summary>
        public static string[] GetLoadingOrder(System.Type type)
        {
            lock (cacheLoadingOrder)
            {
                string[] res = (string[])cacheLoadingOrder[type];
                if (res == null)
                {
                    var result = new StringCollection();
                    var myAttributes = type.GetCustomAttributes(typeof(LoadingOrderAttribute), true);
                    var orders = new string[myAttributes.Length][];
                    for (int i = 0; i < myAttributes.Length; i++)
                    {
                        string[] order = ((LoadingOrderAttribute)myAttributes[i]).Order;
                        orders[i] = order;
                    }

                    res = prv_MakeGraph(orders);
                    cacheLoadingOrder[type] = res;
                    return res;
                }
                else
                {
                    return CopyStringArray(res);
                }
            }
        }

        /// <summary>
        /// Проверка на совместимость объекта данных в  методе, или свойстве, откуда вызвано.
        /// Проверяет мастеровые свойства объектов данных и детейлов.
        /// </summary>
        /// <param name="testObj"></param>
        public static void CheckUsingType(DataObject testObj)
        {
            if (testObj == null)
            {
                return;
            }

            // откуда вызов?
            var stackTrace = new System.Diagnostics.StackTrace();
            var CallMethodInfo = (MethodInfo)stackTrace.GetFrame(1).GetMethod();

            // узнаем что это за метод
            string Name = CallMethodInfo.Name;
            Type DeclMethodType = CallMethodInfo.DeclaringType;
            bool CallMethodIsSpecialName = CallMethodInfo.IsSpecialName;
            Type[] CheckTypes = null;
            string propName = string.Empty;
            if (CallMethodIsSpecialName
                && Name.StartsWith("set_")
                && DeclMethodType.IsSubclassOf(typeof(DataObject)))
            {
                // проверка из свойства объекта данных
                // атрибут должен прописываться у свойства

                // поднимимся по перегруженным методам
                int frameIndex = 2;
                while (frameIndex < stackTrace.FrameCount)
                {
                    System.Reflection.MethodInfo SuperCallMethodInfo = (MethodInfo)stackTrace.GetFrame(frameIndex).GetMethod();
                    if (SuperCallMethodInfo.DeclaringType.IsSubclassOf(DeclMethodType) && CallMethodInfo.Name == SuperCallMethodInfo.Name)
                    {
                        DeclMethodType = SuperCallMethodInfo.DeclaringType;
                        CallMethodInfo = SuperCallMethodInfo;
                        Name = CallMethodInfo.Name;
                        frameIndex++;
                    }
                    else
                    {
                        break;
                    }
                }

                propName = Name.Substring(4);
                CheckTypes = TypeUsageProvider.TypeUsage.GetUsageTypes(DeclMethodType, propName);
            }
            else if
                (!CallMethodIsSpecialName &&
                DeclMethodType.IsSubclassOf(typeof(DetailArray)))
            {
                // проверка из операции ДетэйлАррея
                // атрибут должен прописываться у класса
                object[] atrs = DeclMethodType.GetCustomAttributes(typeof(TypeUsageAttribute), false);
                if (atrs.Length > 0)
                {
                    CheckTypes = ((TypeUsageAttribute)atrs[0]).UseTypes;
                }
            }

            if (CheckTypes != null)
            {
                string allTypes = string.Empty;
                foreach (Type testType in CheckTypes)
                {
                    if (testObj.GetType() == testType)
                    {
                        return;
                    }

                    allTypes += testType.Name + ";";
                }

                throw new IncomatibleCheckingTypeException(DeclMethodType.FullName, propName, testObj.GetType().FullName, allTypes);
            }
        }

        /// <summary>
        /// По массиву нескольких описаний порядков возвращает общий порядок.
        /// </summary>
        private static string[] prv_MakeGraph(string[][] s)
        {
            var scresult = new StringCollection();
            var p1 = new StringCollection();
            var p2 = new StringCollection();
            var po = new StringCollection();

            for (int i = 0; i < s.Length; i++)
            {
                for (int j = 0; j < s[i].Length - 1; j++)
                {
                    p1.Add(s[i][j]);
                    p2.Add(s[i][j + 1]);
                }
            }

            for (int i = 0; i < s.Length; i++)
            {
                for (int j = 0; j < s[i].Length; j++)
                {
                    if (!po.Contains(s[i][j]))
                    {
                        po.Add(s[i][j]);
                    }
                }
            }

            bool bFound = true;
            while (p1.Count > 0)
            {
                while (bFound)
                {
                    bFound = false;
                    for (int i = 0; i < p2.Count; i++)
                    {
                        if (!p1.Contains(p2[i]))
                        {
                            string tmpstring = p2[i];
                            scresult.Insert(0, tmpstring);
                            if (po.Contains(tmpstring))
                            {
                                po.Remove(tmpstring);
                            }

                            for (int j = 0; j < p2.Count; j++)
                            {
                                if (p2[j] == tmpstring)
                                {
                                    p1.RemoveAt(j);
                                    p2.RemoveAt(j);
                                }
                            }

                            bFound = true;
                        }
                    }
                }

                if (p1.Count > 0)
                { // Значит, ещё не конец, надо взять первый попавшийся
                    string tmpstring = p2[0];
                    scresult.Insert(0, tmpstring);
                    if (po.Contains(tmpstring))
                    {
                        po.Remove(tmpstring);
                    }

                    for (int i = 0; i < p2.Count; i++)
                    {
                        if (p2[i] == tmpstring)
                        {
                            p1.RemoveAt(i);
                            p2.RemoveAt(i);
                        }
                    }
                }
            }

            for (int i = 0; i < po.Count; i++)
            { // Дописываем оставшиеся
                scresult.Insert(0, po[i]);
            }

            string[] result = new string[scresult.Count];
            scresult.CopyTo(result, 0);
            return result;
        }

        #endregion

        private static TypeAtrValueCollection cacheGetClassCaptionProperty = new TypeAtrValueCollection();

        /// <summary>
        /// Вернуть свойство - заголовок, установленное атрибутом <see cref="InstanceCaptionPropertyAttribute"/>.
        /// </summary>
        /// <param name="dataobjectType"></param>
        /// <returns></returns>
        public static string GetClassCaptionProperty(System.Type dataobjectType)
        {
            lock (cacheGetClassCaptionProperty)
            {
                object res = cacheGetClassCaptionProperty[dataobjectType];
                if (res != null)
                {
                    return (string)res;
                }
                else
                {
                    string sres;
                    object[] atrs = dataobjectType.GetCustomAttributes(typeof(InstanceCaptionPropertyAttribute), true);
                    if (atrs.Length == 0)
                    {
                        sres = string.Empty;
                    }
                    else
                    {
                        var atr = (InstanceCaptionPropertyAttribute)atrs[0];
                        sres = atr.fieldCaptionProperty;
                    }

                    cacheGetClassCaptionProperty[dataobjectType] = sres;
                    return sres;
                }
            }
        }

        private static TypeAtrValueCollection cacheGetClassImageProperty = new TypeAtrValueCollection();

        /// <summary>
        /// Вернуть свойство-картинку, установленное атрибутом <see cref="ClassImagePropertyAttribute"/>.
        /// </summary>
        /// <param name="dataobjectType"></param>
        /// <returns></returns>
        public static string GetClassImageProperty(System.Type dataobjectType)
        {
            lock (cacheGetClassImageProperty)
            {
                object res = cacheGetClassImageProperty[dataobjectType];
                if (res != null)
                {
                    return (string)res;
                }
                else
                {
                    string sres;
                    object[] atrs = dataobjectType.GetCustomAttributes(typeof(ClassImagePropertyAttribute), true);
                    if (atrs.Length == 0)
                    {
                        sres = string.Empty;
                    }
                    else
                    {
                        var atr = (ClassImagePropertyAttribute)atrs[0];
                        sres = atr.property;
                    }

                    cacheGetClassImageProperty[dataobjectType] = sres;
                    return sres;
                }
            }
        }

        /// <summary>
        /// Получить информацию о всех кэшах Information.
        /// </summary>
        /// <returns></returns>
        public static string GetCachesInfo()
        {
            var sb = new StringBuilder();

            var fis = typeof(Information).GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            foreach (FieldInfo fi in fis)
            {
                if (fi.Name.StartsWith("cache"))
                {
                    sb.Append(fi.Name);

                    object value = fi.GetValue(null);

                    if (value != null)
                    {
                        Type type = value.GetType();
                        PropertyInfo propertyInfo = type.GetProperty("Count");
                        if (propertyInfo != null)
                        {
                            object o = propertyInfo.GetValue(value, null);
                            sb.Append(": " + o);
                        }
                    }

                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        private static TypeAtrValueCollection cacheGetClassCaption = new TypeAtrValueCollection();

        /// <summary>
        /// Вернуть заголовок для класса.
        /// </summary>
        /// <param name="dataobjectType"></param>
        /// <returns></returns>
        public static string GetClassCaption(System.Type dataobjectType)
        {
            lock (cacheGetClassCaption)
            {
                object res = cacheGetClassCaption[dataobjectType];
                if (res != null)
                {
                    return (string)res;
                }
                else
                {
                    string sres;
                    object[] atrs = dataobjectType.GetCustomAttributes(typeof(CaptionAttribute), true);
                    if (atrs.Length == 0)
                    {
                        sres = dataobjectType.Name;
                    }
                    else
                    {
                        var atr = (CaptionAttribute)atrs[0];
                        sres = atr.Value;
                    }

                    cacheGetClassCaption[dataobjectType] = sres;
                    return sres;
                }
            }
        }

        /// <summary>
        /// Преобразовать значение к типу ключей объектов класса.
        /// </summary>
        /// <param name="dataobjecttype"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public static object TranslateValueToPrimaryKeyType(Type dataobjecttype, object value)
        {
            Type keyType = KeyGen.KeyGenerator.KeyType(dataobjecttype);
            Type valueType = value.GetType();
            if (valueType == keyType)
            {
                return value;
            }

            if ((value is Guid) && keyType.Equals(typeof(KeyGen.KeyGuid)))
            {
                return new KeyGen.KeyGuid((Guid)value);
            }

            if (Convertors.InOperatorsConverter.CanConvert(valueType, keyType))
            {
                return Convertors.InOperatorsConverter.Convert(value, keyType);
            }

            throw new PrimaryKeyTypeException();
        }

        /// <summary>
        /// Возвращает общего предка.
        /// </summary>
        /// <param name="testType"></param>
        /// <param name="types"></param>
        /// <returns></returns>
        private static int GetMostCompatibleType(System.Type testType, System.Type[] types)
        {
            for (int i = 0; i < types.Length; i++)
            {
                if (testType == types[i])
                {
                    return i;
                }
            }

            if (testType == typeof(object))
            {
                return -1;
            }
            else
            {
                return GetMostCompatibleType(testType.BaseType, types);
            }
        }

        private static TypePropertyAtrValueCollection cacheGetStorageTypeForType = new TypePropertyAtrValueCollection();

        /// <summary>
        /// Вернуть тип хранения для заданного типа.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="DataServiceType"></param>
        /// <returns></returns>
        public static System.Type GetStorageTypeForType(Type type, Type DataServiceType)
        {
            lock (cacheGetStorageTypeForType)
            {
                string key = DataServiceType.Name;
                System.Type res = (System.Type)cacheGetStorageTypeForType[type, key];
                if (res != null)
                {
                    return res;
                }
                else
                {
                    if (type.IsSubclassOf(typeof(DataObject)))
                    {
                        res = type;
                    }
                    else if (type.IsSubclassOf(typeof(DetailArray)))
                    {
                        res = type;
                    }
                    else
                    {
                        object[] atrs = type.GetCustomAttributes(typeof(StoreInstancesInTypeAttribute), true);
                        res = type;
                        if (atrs.Length > 0)
                        {
                            System.Type[] types = new Type[atrs.Length];
                            for (int i = 0; i < atrs.Length; i++)
                            {
                                types[i] = ((StoreInstancesInTypeAttribute)atrs[i]).DataServiceType;
                            }

                            int atrindex = GetMostCompatibleType(DataServiceType, types);
                            if (atrindex >= 0)
                            {
                                res = ((StoreInstancesInTypeAttribute)atrs[atrindex]).StorageType;
                            }
                        }
                    }

                    // Для генерённых на ходу типов не добавляем в кеш, т.к. они меняются в любой момент (например редактор параметров генерит фиктивный тип для задания параметров и формы параметров)
                    if (type.Assembly.FullName != "TempAssembly, Version=0.0.0.0")
                    {
                        cacheGetStorageTypeForType[type, key] = res;
                    }

                    return res;
                }
            }
        }

        /// <summary>
        /// Вернуть тип хранения для заданного значения.
        /// </summary>
        /// <param name="value"></param>
        /// <param name="DataServiceType"></param>
        /// <returns></returns>
        public static System.Type GetStorageType(object value, System.Type DataServiceType)
        {
            return GetStorageTypeForType(value.GetType(), DataServiceType);
        }

        /// <summary>
        /// Вернуть тип хранения для заданного свойства.
        /// </summary>
        /// <param name="dataobjecttype"></param>
        /// <param name="propname"></param>
        /// <param name="DataServiceType"></param>
        /// <returns></returns>
        public static System.Type GetPropertyStorageType(System.Type dataobjecttype, string propname, System.Type DataServiceType)
        {
            return GetStorageTypeForType(GetPropertyType(dataobjecttype, propname), DataServiceType);
        }

        /// <summary>
        /// Является ли значение пустым (null).
        /// </summary>
        /// <returns></returns>
        public static bool IsEmptyPropertyValue(object value)
        {
            if (value == null)
            {
                return true;
            }

            // В методе EditManager’а была проверка (propertyValue.GetType() == typeof(string) && (string)propertyValue == ""),
            // в DataObject (val == String.Empty)

            if (string.Equals(value, string.Empty))
            {
                return true;
            }

            if (value.GetType().IsEnum && IsEmptyEnumValue(value))
            {
                return true;
            }

            if (value is ISpecialEmptyValue)
            {
                return (value as ISpecialEmptyValue).IsEmptyValue(value);
            }

            return false;
        }

        /// <summary>
        /// Является ли значение перечислимого пустым (null).
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static bool IsEmptyEnumValue(object value)
        {
            try
            {
                if (value == null)
                {
                    return true;
                }

                var propertyType = value.GetType();
                var fields = propertyType.GetFields();
                var usedEmptyEnumValueAttribute = false;
                foreach (var field in fields)
                {
                    if (field.IsSpecialName)
                    {
                        continue;
                    }

                    var atrs = field.GetCustomAttributes(typeof(EmptyEnumValueAttribute), true);

                    // Если текущее значение совпадает с помеченным значением, то покажем таракана.
                    if (atrs != null && atrs.Length > 0)
                    {
                        usedEmptyEnumValueAttribute = true;
                        if (Enum.GetName(propertyType, value) == field.Name)
                        {
                            return true;
                        }
                    }
                }

                var caption = EnumCaption.GetCaptionFor(value);
                if (!usedEmptyEnumValueAttribute && string.IsNullOrEmpty(caption))
                {
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                if (LogService.Log.IsWarnEnabled)
                {
                    LogService.Log.Warn("Ошибка в методе IsEmptyEnumValue.", ex);
                }

                return false;
            }
        }

        /// <summary>
        /// Возвращает все мастеровые объекты данных для указанного объекта данных.
        /// Анализ мастеров происходит по указанному представлению.
        /// Мастер попадет в список возвращаемых, если в представлении указано хотябы одно его свойство,
        /// иначе будет считаться, что загружается только ссылка, а не весь мастер.
        /// </summary>
        /// <param name="viewName">Наименование представления объекта данных.</param>
        /// <param name="dataObject">
        /// Объекта данных, у которого будет искаться указанное представление.
        /// Объект данных должен быть уже загружен по указанному представлению, тогда метод отработает правильно.
        /// </param>
        /// <returns>Список мастеров объекта данных.</returns>
        public static List<DataObject> GetMastersForDataObjectByView(DataObject dataObject, string viewName)
        {
            Type dataObjectType = dataObject.GetType();
            object[] viewAttributes = dataObjectType.GetCustomAttributes(typeof(ViewAttribute), false);
            var viewAttribute = viewAttributes.FirstOrDefault(item => ((ViewAttribute)item).Name == viewName) as ViewAttribute;

            if (viewAttribute == null)
            {
                return new List<DataObject>();
            }

            var result = new List<DataObject>();

            foreach (var property in viewAttribute.Properties)
            {
                string[] partsPropName = property.Split(new[] { '.' }, StringSplitOptions.None);

                // цепочка объектов данных, для вычитки цепочки мастетор (проход по иерархии)
                var allDataObjects = new List<DataObject> { dataObject };

                // перебираем только те мастера у которых указаны свойства для загрузки в представлении
                // иначе считается, что нужно загрузить только ссылку на мастера
                for (int i = 0; i < partsPropName.Length - 1; i++)
                {
                    string masterName = partsPropName[i];
                    DataObject currentDataObject = allDataObjects[i];
                    object master = GetPropValueByName(currentDataObject, masterName);

                    // если это мастер и он загружен
                    if (master != null && master is DataObject)
                    {
                        // то записываем его в цепочку
                        allDataObjects.Add((DataObject)master);

                        // и если его нет ещё в списке результа, то и в результат
                        if (!result.Contains((DataObject)master))
                        {
                            result.Add((DataObject)master);
                        }
                    }
                    else
                    {
                        // иначе считается, что цепочка оборвалась из-за неправильно заданного свойства представления или
                        // из-за того, что мастер не был загружен
                        break;
                    }
                }
            }

            return result;
        }

        #region PropertySupport

        /// <summary>
        /// Извлечение свойства внутри текущего класса.
        /// <code>
        /// // Пример использования:
        /// ExtractPropertyName[T](() => objectInstance.Name); // вернет "Name", T - тип объекта objectInstance
        /// ExtractPropertyName[T](() => objectInstance.Master.Name); // вернет "Name", T - тип объекта objectInstance
        /// </code>
        /// </summary>
        /// <typeparam name="TSource"> Тип класса - источника. </typeparam>
        /// <param name="propertyExpression"> Лямбда - выражение для доступа к свойству. </param>
        /// <returns> Имя свойства (одиночное!). </returns>
        public static string ExtractPropertyName<TSource>(Expression<Func<TSource>> propertyExpression)
        {
            return InternalExtractPropertyName(propertyExpression);
        }

        /// <summary>
        /// Explicit извлечение свойства по типу.
        /// <code>
        /// // Пример использования:
        /// ExtractPropertyName(a =&gt; a.Name); // вернет "Name"
        /// ExtractPropertyName(a =&gt; a.b.c.Name); // вернет "Name"
        /// </code>
        /// </summary>
        /// <typeparam name="TSource">
        /// Тип класса - источника.
        /// </typeparam>
        /// <param name="propertyExpression">
        /// Лямбда - выражение для доступа к свойству.
        /// </param>
        /// <returns>
        /// Имя свойства (одиночное!).
        /// </returns>
        public static string ExtractPropertyName<TSource>(Expression<Func<TSource, object>> propertyExpression)
        {
            return InternalExtractPropertyName(propertyExpression);
        }

        #region ExtractPropertyPath

        /// <summary>
        /// Рекурсивный метод получения пути для свойства, заданного через вложенную лямбду.
        /// Лямбда-выражение может содержать вложенные обращения к мастерам.
        /// <code>
        /// // Пример использования:
        /// ExtractPropertyPath[T](() => objectInstance.Name); // вернет "Name", T - тип объекта objectInstance
        /// ExtractPropertyPath[T](() => objectInstance.Master.Name); // вернет "Master.Name", T - тип объекта objectInstance
        /// </code>
        /// </summary>
        /// <typeparam name="TProperty"> Тип свойства. </typeparam>
        /// <param name="propertyExpression"> Лямбда - выражение для доступа к свойству. </param>
        /// <returns> Полный путь к свойству (разделение через точку). </returns>
        public static string ExtractPropertyPath<TProperty>(Expression<Func<TProperty>> propertyExpression)
        {
            return InternalExtractPropertyPath(propertyExpression);
        }

        /// <summary>
        /// Рекурсивный метод получения пути для свойства, заданного через вложенную лямбду.
        /// Лямбда-выражение может содержать вложенные обращения к мастерам.
        /// <code>
        /// // Пример использования:
        /// ExtractPropertyPath(a => a.Name); // вернет "Name"
        /// ExtractPropertyPath(a => a.b.c.Name); // вернет "b.c.Name"
        /// </code>
        /// </summary>
        /// <typeparam name="TSource"> Тип класса - источника. </typeparam>
        /// <param name="propertyExpression"> Лямбда - выражение для доступа к свойству. </param>
        /// <returns>
        /// Полный путь к свойству (разделение через точку).
        /// </returns>
        public static string ExtractPropertyPath<TSource>(Expression<Func<TSource, object>> propertyExpression)
        {
            return InternalExtractPropertyPath(propertyExpression);
        }

        /// <summary>
        /// Рекурсивный метод получения пути для свойства, заданного через вложенную лямбду.
        /// Лямбда-выражение может содержать вложенные обращения к мастерам.
        /// <code>
        /// Пример использования:
        /// ExtractPropertyPath(a => a.Name); // вернет "Name"
        /// ExtractPropertyPath(a => a.b.c.Name); // вернет "b.c.Name"
        /// ExtractPropertyPath[T](() => objectInstance.Name); // вернет "Name", T - тип объекта objectInstance
        /// ExtractPropertyPath[T](() => objectInstance.Master.Name); // вернет "Master.Name", T - тип объекта objectInstance
        /// </code>
        /// </summary>
        /// <param name="propertyExpression"> Лямбда - выражение для доступа к свойству. </param>
        /// <returns> Полный путь к свойству (разделение через точку). </returns>
        private static string InternalExtractPropertyPath(LambdaExpression propertyExpression)
        {
            var body = ExtractMemberExpression(propertyExpression);
            var result = InternalExtractPropertyPath(body);
            return result;
        }

        /// <summary>
        /// Рекурсивный метод получения пути для свойства, заданного через вложенную лямбду.
        /// Лямбда-выражение может содержать вложенные обращения к мастерам.
        /// </summary>
        /// <param name="propertyExpression"> Лямбда - выражение для доступа к свойству. </param>
        /// <returns> Полный путь к свойству (разделение через точку). </returns>
        private static string InternalExtractPropertyPath(MemberExpression propertyExpression)
        {
            if (propertyExpression == null)
            {
                return null;
            }

            var currentPathItem = propertyExpression.Member.Name;
            var itemsBefore = InternalExtractPropertyPath(propertyExpression.Expression as MemberExpression);

            return string.IsNullOrEmpty(itemsBefore) ? currentPathItem : string.Format("{0}.{1}", itemsBefore, currentPathItem);
        }

        #endregion

        /// <summary>
        /// Explicit извлечение свойства по типу.
        /// </summary>
        /// <typeparam name="TSource"> Тип класса - источника. </typeparam>
        /// <param name="propertyExpression"> Лямбда - выражение для доступа к свойству. </param>
        /// <returns> <see cref="PropertyInfo"/> свойства (самого последнего). </returns>
        public static PropertyInfo ExtractPropertyInfo<TSource>(Expression<Func<TSource, object>> propertyExpression)
        {
            return InternalExtractPropertyInfo(propertyExpression);
        }

        /// <summary>
        /// The internal extract property info.
        /// </summary>
        /// <param name="propertyExpression">
        /// The property expression.
        /// </param>
        /// <returns>
        /// The <see cref="PropertyInfo"/>.
        /// </returns>
        /// <exception cref="ArgumentException"> Выражение должно являться свойством. </exception>
        private static PropertyInfo InternalExtractPropertyInfo(LambdaExpression propertyExpression)
        {
            var body = ExtractMemberExpression(propertyExpression);
            var member = body.Member as PropertyInfo;
            if (member == null)
            {
                throw new ArgumentException(@"Выражение не является свойством", "propertyExpression");
            }

            return member;
        }

        /// <summary>
        /// Метод получения MemberExpression из лямбда-выражения.
        /// </summary>
        /// <param name="propertyExpression"> Выражение для получения значения свойства. </param>
        /// <returns> <see cref="MemberExpression"/> для получения значения. </returns>
        private static MemberExpression ExtractMemberExpression(LambdaExpression propertyExpression)
        {
            if (propertyExpression == null)
            {
                throw new ArgumentNullException("propertyExpression");
            }

            var body = propertyExpression.Body;
            var memberBody = body as MemberExpression;
            if (memberBody == null && (body.NodeType == ExpressionType.Convert || body.NodeType == ExpressionType.ConvertChecked))
            {
                memberBody = ((UnaryExpression)body).Operand as MemberExpression;
            }

            if (memberBody == null)
            {
                throw new ArgumentException(@"Выражение не является членом класса", "propertyExpression");
            }

            return memberBody;
        }

        /// <summary>
        /// Получение названия свойства по лямбде.
        /// </summary>
        /// <param name="propertyExpression">
        /// The property Expression.
        /// </param>
        /// <returns>
        /// The <see cref="string"/>.
        /// </returns>
        private static string InternalExtractPropertyName(LambdaExpression propertyExpression)
        {
            var member = InternalExtractPropertyInfo(propertyExpression);
            if (member.GetGetMethod(true).IsStatic)
            {
                throw new ArgumentException(@"Выражение ссылается на статическое свойство", "propertyExpression");
            }

            return member.Name;
        }

        #endregion PropertySupport

        /// <summary>
        /// Получить дату компиляции сборки.
        /// </summary>
        /// <param name="filePath">
        /// Путь до сборки, для которой будет возвращена дата компиляции.
        /// </param>
        /// <returns>
        /// Дата компиляции сборки.
        /// </returns>
        public static DateTime RetrieveLinkerTimestamp(string filePath)
        {
            const int peHeaderOffset = 60;
            const int linkerTimestampOffset = 8;
            byte[] b = new byte[2048];
            System.IO.Stream s = null;

            try
            {
                s = new System.IO.FileStream(filePath, System.IO.FileMode.Open, System.IO.FileAccess.Read);
                s.Read(b, 0, 2048);
            }
            finally
            {
                if (s != null)
                {
                    s.Close();
                }
            }

            int i = BitConverter.ToInt32(b, peHeaderOffset);
            int secondsSince1970 = System.BitConverter.ToInt32(b, i + linkerTimestampOffset);
            var dt = new DateTime(1970, 1, 1, 0, 0, 0);
            dt = dt.AddSeconds(secondsSince1970);
            dt = dt.AddHours(TimeZone.CurrentTimeZone.GetUtcOffset(dt).Hours);
            return dt;
        }

        /// <summary>
        /// Получить дату линковки сборки, которая содержит метод, вызывающий данный метод.
        /// </summary>
        /// <returns>Дата линковки сборки.</returns>
        public static DateTime RetrieveLinkerTimestamp()
        {
            string filePath = Assembly.GetCallingAssembly().Location;
            return RetrieveLinkerTimestamp(filePath);
        }

        /// <summary>
        /// Получить описание лукапа из атрибутов объекта данных.
        /// </summary>
        /// <param name="view">Представление.</param>
        /// <param name="masterName">Имя мастера.</param>
        /// <returns></returns>
        public static MasterViewDefineAttribute GetLookupCustomizationString(View view, string masterName)
        {
            Type t = view.DefineClassType;

            object[] attributes = t.GetCustomAttributes(typeof(MasterViewDefineAttribute), false);

            return attributes.Cast<MasterViewDefineAttribute>().FirstOrDefault(attr => attr.ViewName == view.Name && attr.MasterName == masterName);
        }

        // ToDo: Представленный ниже метод механически выделен из метода SetPropValueByName. Необходимо произвести рефакторинг этих методов и вызывать ParsePropertyValue в SetPropValueByName для пакринга строкового значения.

        /// <summary>
        /// Метод преобразования строкового значения с объектное значение.
        /// </summary>
        /// <param name="tp">Тип объекта данных.</param>
        /// <param name="propertyName">Имя свойства, значение которого необходимо преобразовать.</param>
        /// <param name="value">Строковое значение свойства.</param>
        /// <returns>Преобразованное в тип свойства строковое значение.</returns>
        public static object ParsePropertyValue(Type tp, string propertyName, string value)
        {
            if (value == null)
            {
                return null;
            }

            Type propertyType;

            PropertyInfo pi = tp.GetProperty(propertyName);
            if (pi == null)
            {
                throw new CantFindPropertyException(propertyName, tp);
            }

            if (propertyName != "__PrimaryKey")
            {
                propertyType = GetPropertyType(tp, propertyName);
            }
            else
            {
                propertyType = KeyGen.KeyGenerator.KeyType(tp);
            }

            if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                // Сервис данных обрабатывает string.Empty как null-значение, так что будем присваивать его напрямую. Также это закрывает проблему с десериализацией объектов, когда null записан как string.Empty
                if (value == string.Empty)
                {
                    return null;
                }

                propertyType = Nullable.GetUnderlyingType(propertyType);
            }

            if (propertyType == typeof(string))
            {
                if (TrimmedStringStorage(tp, propertyName))
                {
                    value = value.Trim();
                }

                return value;
            }

            object newPropVal;
            string propValString = value;
            if (propertyType.IsEnum)
            {
                propValString = propValString.Trim();
                newPropVal = EnumCaption.GetValueFor(propValString, propertyType);
                return newPropVal;
            }

            if (propertyType != typeof(object))
            {
                if (propertyType == typeof(DateTime))
                {
                    DateTime dtVal;
                    if (DateTime.TryParse(propValString, out dtVal))
                    {
                        return dtVal;
                    }

                    IFormatProvider culture = new System.Globalization.CultureInfo("ru-RU", false);
                    var dtVal1 = DateTime.Parse(propValString, culture);
                    return dtVal1;
                }

                if (propertyType.GetMethod("Parse", BindingFlags.Static | BindingFlags.Public, null, new Type[] { typeof(string), typeof(IFormatProvider) }, null) != null)
                {
                    try
                    {
                        newPropVal = propertyType.InvokeMember("Parse", BindingFlags.InvokeMethod, null, null, new object[2] { propValString, System.Globalization.NumberFormatInfo.InvariantInfo });
                        return newPropVal;
                    }
                    catch
                    {
                        throw new InvalidCastException();
                    }
                }

                if (propertyType.GetMethod("Parse", BindingFlags.Static | BindingFlags.Public, null, new Type[] { typeof(string) }, null) != null)
                {
                    newPropVal = propertyType.InvokeMember("Parse", BindingFlags.InvokeMethod, null, null, new object[1] { propValString });
                    return newPropVal;
                }

                MethodInfo opImpl = propertyType.GetMethod("op_Implicit", new Type[] { typeof(string) });
                if (opImpl != null && opImpl.IsSpecialName)
                {
                    newPropVal = opImpl.Invoke(null, new object[] { propValString });
                }
                else
                {
                    MethodInfo opExpl = propertyType.GetMethod("op_Explicit", new Type[] { typeof(string) });
                    if (opExpl != null && opExpl.IsSpecialName)
                    {
                        newPropVal = opExpl.Invoke(null, new object[] { propValString });
                    }
                    else
                    {
                        throw new InvalidCastException();
                    }
                }

                return newPropVal;
            }

            newPropVal = value;
            return newPropVal;
        }

        /// <summary>
        /// Проверка прав на атрибуты объекта. Метод является оберткой для метода CheckAccessToAttribute интерфейса <see cref="ISecurityManager"/> и используется для проверки прав в Get'ерах вычислимых свойств DataObject.
        /// Обработка мастеров не производится.
        /// </summary>
        /// <param name="type">Тип объекта данных.</param>
        /// <param name="propertyName">Имя свойства объекта данных, на которое проверяются права.</param>
        /// <param name="deniedAccessValue">Значение атрибута при отсутствии прав.</param>
        /// <returns>Если у текущего пользователя есть права на доступ к указанному свойству, то <c>true</c>, иначе - <c>false</c>.</returns>
        public static bool CheckAccessToAttribute(Type type, string propertyName, out object deniedAccessValue)
        {
            deniedAccessValue = null;

            // Регулярное выражение для удаления кавычек и других символов из sql-константы значения по умолчанию.
            const string sqlValuePattern = @"(?<=(['#])).*(?=\1)";

            string expression = null;

            var expressions = GetExpressionForProperty(type, propertyName);

            // Определить какой из DataService используется не предоставляется возможным,
            // в большинстве случаев DataServiceExpression будет один.
            // В случае нескольких DataServiceExpression, права все равно должны совпадать.
            if (expressions.Count > 0)
            {
                expression = (string)expressions[0];
            }
            else
            {
                return true;
            }

            string deniedAccessValueInString = null;

            // Получаем текущую неименованную реализацию ISecurityManager из Unity.
            IUnityContainer container = UnityFactory.GetContainer();
            var securityManager = container.Resolve<ISecurityManager>();
            var result = securityManager.CheckAccessToAttribute(expression, out deniedAccessValueInString);

            if (!result && !string.IsNullOrEmpty(deniedAccessValueInString))
            {
                var match = Regex.Match(deniedAccessValueInString, sqlValuePattern);
                if (match.Success)
                {
                    deniedAccessValueInString = match.Value;
                }
            }

            deniedAccessValue = ParsePropertyValue(type, propertyName, deniedAccessValueInString);

            return result;
        }

        /// <summary>
        /// Получение свойств, входящих в состав выражения DataServiceExpression(считается, что свойство заключено в @).
        /// Код метода перенесен из <see cref="ICSSoft.STORMNET.Business.SQLDataService"/>.
        /// </summary>
        /// <param name="expression">Выражение DataServiceExpression.</param>
        /// <param name="namespacewithpoint">Пространство имен, которое при указании добавляется к каждому свойству.</param>
        /// <returns>Список свойств.</returns>
        public static string[] GetPropertiesInExpression(string expression, string namespacewithpoint)
        {
            System.Collections.ArrayList sc = new System.Collections.ArrayList();

            string[] expressarr = expression.Split('@');

            // string result = "";
            int nextIndex = 1;
            for (int i = 0; i < expressarr.Length; i++)
            {
                if (i != nextIndex)
                {
                    // result += expressarr[i];
                }
                else
                {
                    if (expressarr[nextIndex] == string.Empty)
                    {
                        // result+="@";
                        nextIndex++;
                    }
                    else
                    {
                        if (namespacewithpoint != string.Empty)
                        {
                            sc.Add(namespacewithpoint + expressarr[nextIndex]);
                        }

                        // result+=PutIdentifierIntoBrackets(namespacewithpoint+expressarr[nextIndex]);
                        else
                        {
                            // result+=PutIdentifierIntoBrackets(expressarr[nextIndex]);
                            sc.Add(expressarr[nextIndex]);
                        }

                        nextIndex += 2;
                    }
                }
            }

            // return "("+result+")";
            return (string[])sc.ToArray(typeof(string));
        }

        /// <summary>
        /// Делегат для проверки совместимости хранилищ свойств у указанных типов.
        /// </summary>
        public static CheckCompatiblePropertyStorageTypesDelegate CheckCompatiblePropertyStorageTypesDelegate { get; set; }

        /// <summary>
        /// Проверить совместимость хранилищ свойств у указанных типов.
        /// </summary>
        /// <param name="dobjType">Тип проверяемого объекта данных.</param>
        /// <param name="propName">Проверяемое свойство.</param>
        /// <param name="propValType">Тип значения, присвоенного свойству.</param>
        /// <param name="allowedType">Тип, являющийся допустимым для свойства.</param>
        /// <returns>Возвращает <c>true</c>, если совместимы.</returns>
        public static bool CheckCompatiblePropertyStorageTypes(Type dobjType, string propName, Type propValType, Type allowedType)
        {
            if (propValType == null || allowedType == null)
            {
                return false;
            }

            return propValType == allowedType
                   || GetClassStorageName(propValType) == GetClassStorageName(allowedType)
                   || CheckCompatiblePropertyStorageTypesDelegate?.Invoke(dobjType, propName, propValType, allowedType) == true;
        }
    }
    #endregion
}
