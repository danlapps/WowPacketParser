﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Text;
using Wintellect.PowerCollections;
using WowPacketParser.Enums;
using WowPacketParser.Misc;
using WowPacketParser.Store;
using WowPacketParser.Store.Objects;

namespace WowPacketParser.SQL
{
    public static class SQLDatabase
    {
        public static readonly Dictionary<StoreNameType, Dictionary<int, string>> NameStores = new Dictionary<StoreNameType, Dictionary<int, string>>();
        public static readonly ICollection<Tuple<uint, BroadcastText>> BroadcastTextStores = new List<Tuple<uint, BroadcastText>>();
        public static readonly Dictionary<uint, CreatureDifficulty> CreatureDifficultyStores = new Dictionary<uint, CreatureDifficulty>();


        private static readonly StoreNameType[] ObjectTypes =
        {
            StoreNameType.Spell,
            StoreNameType.Map,
            StoreNameType.LFGDungeon,
            StoreNameType.Battleground,
            StoreNameType.Unit,
            StoreNameType.GameObject,
            StoreNameType.Item,
            StoreNameType.Quest,
            StoreNameType.Zone,
            StoreNameType.Area,
            StoreNameType.Player,
            StoreNameType.Achievement
        };

        public static void GrabNameData()
        {
            if (!SQLConnector.Connected())
                throw new DataException("Cannot get DB data without an active DB connection.");

            foreach (var objectType in ObjectTypes)
                NameStores.Add(objectType, GetDict<int, string>(string.Format("SELECT `Id`, `Name` FROM `ObjectNames` WHERE `ObjectType`='{0}';", objectType)));
        }

        public static void LoadSQL()
        {
            if (!SQLConnector.Connected())
                throw new DataException("Cannot get DB data without an active DB connection.");

            var startTime = DateTime.Now;

            LoadBroadcastText();
            LoadCreatureDifficulty();

            var endTime = DateTime.Now;
            var span = endTime.Subtract(startTime);
            Trace.WriteLine(String.Format("SQL loaded in {0}.", span.ToFormattedString()));
        }

        private static void LoadBroadcastText()
        {
            var query = new StringBuilder(string.Format("SELECT ID, Language, MaleText, FemaleText, EmoteID1, EmoteID2, EmoteID3, EmoteDelay1, EmoteDelay2, EmoteDelay3, SoundId, UnkEmoteID, Type FROM {0}.broadcast_text;", Settings.HotfixesDatabase));
            using (var reader = SQLConnector.ExecuteQuery(query.ToString()))
            {
                if (reader == null)
                    return;

                while (reader.Read())
                {
                    var broadcastText = new BroadcastText();

                    uint id = Convert.ToUInt32(reader["Id"]);

                    broadcastText.language = Convert.ToInt32(reader["Language"]);
                    broadcastText.MaleText = Convert.ToString(reader["MaleText"]);
                    broadcastText.FemaleText = Convert.ToString(reader["FemaleText"]);

                    broadcastText.emoteID0 = Convert.ToUInt32(reader["EmoteID1"]);
                    broadcastText.emoteID1 = Convert.ToUInt32(reader["EmoteID2"]);
                    broadcastText.emoteID2 = Convert.ToUInt32(reader["EmoteID3"]);

                    broadcastText.emoteDelay0 = Convert.ToUInt32(reader["EmoteDelay1"]);
                    broadcastText.emoteDelay1 = Convert.ToUInt32(reader["EmoteDelay2"]);
                    broadcastText.emoteDelay2 = Convert.ToUInt32(reader["EmoteDelay3"]);

                    broadcastText.soundId = Convert.ToUInt32(reader["SoundId"]);
                    broadcastText.unk1 = Convert.ToUInt32(reader["UnkEmoteID"]);
                    broadcastText.unk2 = Convert.ToUInt32(reader["Type"]);

                    var tuple = Tuple.Create(id, broadcastText);
                    BroadcastTextStores.Add(tuple);
                }
            }
        }

        private static void LoadCreatureDifficulty()
        {
            //                                                  0       1           2           3       4           5       6       7       8       9       10
            var query = new StringBuilder(string.Format("SELECT ID, CreatureID, FactionID, Expansion, MinLevel, MaxLevel, Flags1, Flags2, Flags3, Flags4, Flags5 FROM {0}.creature_difficulty;", Settings.HotfixesDatabase));
            using (var reader = SQLConnector.ExecuteQuery(query.ToString()))
            {
                if (reader == null)
                    return;

                while (reader.Read())
                {
                    var creatureDifficulty = new CreatureDifficulty();

                    uint id = (uint)reader.GetValue(0);

                    creatureDifficulty.CreatureID = (uint)reader.GetValue(1);
                    creatureDifficulty.FactionID = (uint)reader.GetValue(2);

                    creatureDifficulty.Expansion = (int)reader.GetValue(3);
                    creatureDifficulty.MinLevel = (int)reader.GetValue(4);
                    creatureDifficulty.MaxLevel = (int)reader.GetValue(5);

                    creatureDifficulty.Flags = new uint[5];
                    for (int i = 0; i < 5; i++)
                        creatureDifficulty.Flags[i] = (uint)reader.GetValue(i + 6);

                    CreatureDifficultyStores.Add(id, creatureDifficulty);
                }
            }
        }

        // Returns a dictionary from a DB query with two parameters (e.g <creature_entry, creature_name>)
        // TODO: Drop this and use the GetDict<T, TK> method below
        private static Dictionary<T, TK> GetDict<T, TK>(string query)
        {
            using (var reader = SQLConnector.ExecuteQuery(query))
            {
                if (reader == null)
                    return null;

                var dict = new Dictionary<T, TK>();

                while (reader.Read())
                    dict.Add((T)reader.GetValue(0), (TK)reader.GetValue(1));

                return dict;
            }
        }

        /// <summary>
        /// Gets from `world` database a dictionary of the given struct/class.
        /// Structs fields type must match the type of the DB columns.
        /// DB columns names are set by using DBFieldNameAttribute.
        /// </summary>
        /// <typeparam name="T">Type of the elements of the list of entries (usually uint)</typeparam>
        /// <typeparam name="TK">Type of the struct</typeparam>
        /// <param name="entries">List of entries to select from DB</param>
        /// <param name="primaryKeyName"></param>
        /// <param name="database"></param>
        /// <returns>Dictionary of structs of type TK</returns>
        public static StoreDictionary<T, TK> GetDict<T, TK>(List<T> entries, string primaryKeyName = "entry", string database = null)
        {
            if (entries.Count == 0)
                return null;

            // TODO: Add new config option "Verify data against DB"
            if (!SQLConnector.Enabled)
                return null;

            var tableAttrs = (DBTableNameAttribute[])typeof(TK).GetCustomAttributes(typeof(DBTableNameAttribute), false);
            if (tableAttrs.Length <= 0)
                return null;
            var tableName = tableAttrs[0].Name;

            var fields = Utilities.GetFieldsAndAttribute<TK, DBFieldNameAttribute>();
            fields.RemoveAll(field => field.Item2.Name == null);

            var fieldCount = 1;
            var fieldNames = new StringBuilder();
            fieldNames.Append(SQLUtil.AddBackQuotes(primaryKeyName) + ",");
            foreach (var field in fields)
            {
                fieldNames.Append(field.Item2);
                fieldNames.Append(",");
                fieldCount += field.Item2.Count;
            }

            var query = string.Format("SELECT {0} FROM {1}.{2} WHERE {3} IN ({4})",
                fieldNames.ToString().TrimEnd(','), database ?? Settings.TDBDatabase, tableName, primaryKeyName, String.Join(",", entries));

            var dict = new Dictionary<T, TK>(entries.Count);

            using (var reader = SQLConnector.ExecuteQuery(query))
            {
                if (reader == null)
                    return null;

                while (reader.Read())
                {
                    var instance = (TK)Activator.CreateInstance(typeof(TK));

                    var values = new object[fieldCount];
                    var count = reader.GetValues(values);
                    if (count != fieldCount)
                        throw new InvalidConstraintException(
                            "Number of fields from DB is different of the number of fields with DBFieldName attribute");

                    var i = 1;
                    foreach (var field in fields)
                    {
                        if (values[i] is DBNull && field.Item1.FieldType == typeof(string))
                            field.Item1.SetValueDirect(__makeref(instance), string.Empty);
                        else if (field.Item1.FieldType.BaseType == typeof(Enum))
                            field.Item1.SetValueDirect(__makeref(instance), Enum.Parse(field.Item1.FieldType, values[i].ToString()));
                        else if (field.Item1.FieldType.BaseType == typeof(Array))
                        {
                            var arr = Array.CreateInstance(field.Item1.FieldType.GetElementType(), field.Item2.Count);

                            for (var j = 0; j < arr.Length; j++)
                            {
                                var elemType = arr.GetType().GetElementType();

                                var val = elemType.IsEnum ?
                                    Enum.Parse(elemType, values[i + j].ToString()) :
                                    Convert.ChangeType(values[i + j], elemType);

                                arr.SetValue(val, j);
                            }
                            field.Item1.SetValueDirect(__makeref(instance), arr);
                        }
                        else if (field.Item1.FieldType == typeof(bool))
                            field.Item1.SetValueDirect(__makeref(instance), Convert.ToBoolean(values[i]));
                        else
                            field.Item1.SetValueDirect(__makeref(instance), values[i]);

                        i += field.Item2.Count;
                    }

                    T key = (T)values[0];
                    if (!dict.ContainsKey(key))
                        dict.Add(key, instance);
                }
            }

            return new StoreDictionary<T, TK>(dict);
        }

        /// <summary>
        /// Gets from `world` database a dictionary of the given struct/class.
        /// Structs fields type must match the type of the DB columns.
        /// DB columns names are set by using DBFieldNameAttribute.
        /// </summary>
        /// <typeparam name="T">Type of the first element of the list of entries (usually uint)</typeparam>
        /// <typeparam name="TG">Type of the second element of the list of entries (usually uint)</typeparam>
        /// <typeparam name="TK">Type of the struct</typeparam>
        /// <param name="entries">List of entries to select from DB</param>
        /// <param name="primaryKeyName1">Name of the first primary key</param>
        /// <param name="primaryKeyName2">Name of the second primary key</param>
        /// <returns>Dictionary of structs of type TK</returns>
        public static StoreDictionary<Tuple<T, TG>, TK> GetDict<T, TG, TK>(List<Tuple<T, TG>> entries, string primaryKeyName1, string primaryKeyName2) where T : struct where TG : struct
        {
            if (entries.Count == 0)
                return null;

            // TODO: Add new config option "Verify data against DB"
            if (!SQLConnector.Enabled)
                return null;

            var tableAttrs = (DBTableNameAttribute[])typeof(TK).GetCustomAttributes(typeof(DBTableNameAttribute), false);
            if (tableAttrs.Length <= 0)
                return null;
            var tableName = tableAttrs[0].Name;

            var fields = Utilities.GetFieldsAndAttribute<TK, DBFieldNameAttribute>();
            fields.RemoveAll(field => field.Item2.Name == null);

            var fieldCount = 2;
            var fieldNames = new StringBuilder();
            fieldNames.Append(primaryKeyName1 + ",");
            fieldNames.Append(primaryKeyName2 + ",");
            foreach (var field in fields)
            {
                fieldNames.Append(field.Item2);
                fieldNames.Append(",");
                fieldCount += field.Item2.Count;
            }

            // WHERE (a = x1 AND b = y1) OR (a = x2 AND b = y2) OR ...

            var whereClause = new StringBuilder();
            var ji = 0;
            foreach (var tuple in entries)
            {
                ji += 1;
                whereClause.Append("(")
                    .Append(primaryKeyName1)
                    .Append(" = ")
                    .Append(Convert.ToInt64(tuple.Item1))
                    .Append(" AND ")
                    .Append(primaryKeyName2)
                    .Append(" = ")
                    .Append(Convert.ToInt64(tuple.Item2))
                    .Append(")");
                if (ji != entries.Count)
                    whereClause.Append(" OR ");
            }

            var query = string.Format("SELECT {0} FROM {1}.{2} WHERE {3}",
                fieldNames.ToString().TrimEnd(','), Settings.TDBDatabase, tableName, whereClause);

            var dict = new Dictionary<Tuple<T, TG>, TK>(entries.Count);

            using (var reader = SQLConnector.ExecuteQuery(query))
            {
                if (reader == null)
                    return null;

                while (reader.Read())
                {
                    var instance = (TK)Activator.CreateInstance(typeof(TK));

                    var values = new object[fieldCount];
                    var count = reader.GetValues(values);
                    if (count != fieldCount)
                        throw new InvalidConstraintException(
                            "Number of fields from DB is different of the number of fields with DBFieldName attribute");

                    var i = 2;
                    foreach (var field in fields)
                    {
                        if (values[i] is DBNull && field.Item1.FieldType == typeof(string))
                            field.Item1.SetValueDirect(__makeref(instance), string.Empty);
                        else if (field.Item1.FieldType.BaseType == typeof(Enum))
                            field.Item1.SetValueDirect(__makeref(instance), Enum.Parse(field.Item1.FieldType, values[i].ToString()));
                        else if (field.Item1.FieldType.BaseType == typeof(Array))
                        {
                            var arr = Array.CreateInstance(field.Item1.FieldType.GetElementType(), field.Item2.Count);

                            for (var j = 0; j < arr.Length; j++)
                            {
                                var elemType = arr.GetType().GetElementType();

                                var val = elemType.IsEnum ?
                                    Enum.Parse(elemType, values[i + j].ToString()) :
                                    Convert.ChangeType(values[i + j], elemType);

                                arr.SetValue(val, j);
                            }
                            field.Item1.SetValueDirect(__makeref(instance), arr);
                        }
                        else if (field.Item1.FieldType == typeof(bool))
                            field.Item1.SetValueDirect(__makeref(instance), Convert.ToBoolean(values[i]));
                        else
                            field.Item1.SetValueDirect(__makeref(instance), values[i]);

                        i += field.Item2.Count;
                    }

                    T key1;
                    TG key2;
                    if (typeof (T).IsEnum)
                        key1 = (T) Enum.ToObject(typeof (T), values[0]);
                    else
                        key1 = (T) values[0];
                    if (typeof (TG).IsEnum)
                        key2 = (TG)Enum.ToObject(typeof(TG), values[1]);
                    else
                        key2 = (TG)values[1];

                    var key = Tuple.Create(key1, key2);
                    if (!dict.ContainsKey(key))
                        dict.Add(key, instance);
                }
            }

            return new StoreDictionary<Tuple<T, TG>, TK>(dict);
        }

        public static StoreMulti<Tuple<T, TG>, TK> GetDictMulti<T, TG, TK>(List<Tuple<T, TG>> entries, string primaryKeyName1, string primaryKeyName2)
        {
            if (entries.Count == 0)
                return null;

            // TODO: Add new config option "Verify data against DB"
            if (!SQLConnector.Enabled)
                return null;

            var tableAttrs = (DBTableNameAttribute[])typeof(TK).GetCustomAttributes(typeof(DBTableNameAttribute), false);
            if (tableAttrs.Length <= 0)
                return null;
            var tableName = tableAttrs[0].Name;

            var fields = Utilities.GetFieldsAndAttribute<TK, DBFieldNameAttribute>();
            fields.RemoveAll(field => field.Item2.Name == null);

            var fieldCount = 2;
            var fieldNames = new StringBuilder();
            fieldNames.Append(primaryKeyName1 + ",");
            fieldNames.Append(primaryKeyName2 + ",");
            foreach (var field in fields)
            {
                fieldNames.Append(field.Item2);
                fieldNames.Append(",");
                fieldCount += field.Item2.Count;
            }

            // WHERE (a = x1 AND b = y1) OR (a = x2 AND b = y2) OR ...

            var whereClause = new StringBuilder();
            var ji = 0;
            foreach (var tuple in entries)
            {
                ji += 1;
                whereClause.Append("(")
                    .Append(primaryKeyName1)
                    .Append(" = ")
                    .Append(tuple.Item1)
                    .Append(" AND ")
                    .Append(primaryKeyName2)
                    .Append(" = ")
                    .Append(tuple.Item2)
                    .Append(")");
                if (ji != entries.Count)
                    whereClause.Append(" OR ");
            }

            var query = string.Format("SELECT {0} FROM {1}.{2} WHERE {3}",
                fieldNames.ToString().TrimEnd(','), Settings.TDBDatabase, tableName, whereClause);

            var dict = new MultiDictionary<Tuple<T, TG>, TK>(true);

            using (var reader = SQLConnector.ExecuteQuery(query))
            {
                if (reader == null)
                    return null;

                while (reader.Read())
                {
                    var instance = (TK)Activator.CreateInstance(typeof(TK));

                    var values = new object[fieldCount];
                    var count = reader.GetValues(values);
                    if (count != fieldCount)
                        throw new InvalidConstraintException(
                            "Number of fields from DB is different of the number of fields with DBFieldName attribute");

                    var i = 2;
                    foreach (var field in fields)
                    {
                        if (values[i] is DBNull && field.Item1.FieldType == typeof(string))
                            field.Item1.SetValueDirect(__makeref(instance), string.Empty);
                        else if (field.Item1.FieldType.BaseType == typeof(Enum))
                            field.Item1.SetValueDirect(__makeref(instance), Enum.Parse(field.Item1.FieldType, values[i].ToString()));
                        else if (field.Item1.FieldType.BaseType == typeof(Array))
                        {
                            var arr = Array.CreateInstance(field.Item1.FieldType.GetElementType(), field.Item2.Count);

                            for (var j = 0; j < arr.Length; j++)
                            {
                                var elemType = arr.GetType().GetElementType();
                                var val = Convert.ChangeType(values[i + j], elemType);

                                arr.SetValue(val, j);
                            }
                            field.Item1.SetValueDirect(__makeref(instance), arr);
                        }
                        else if (field.Item1.FieldType == typeof(bool))
                            field.Item1.SetValueDirect(__makeref(instance), Convert.ToBoolean(values[i]));
                        else
                            field.Item1.SetValueDirect(__makeref(instance), values[i]);

                        i += field.Item2.Count;
                    }

                    var key = Tuple.Create((T)values[0], (TG)values[1]);
                    if (!dict.ContainsKey(key))
                        dict.Add(key, instance);
                }
            }

            return new StoreMulti<Tuple<T, TG>, TK>(dict);
        }
    }
}
