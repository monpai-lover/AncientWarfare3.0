using System;
using System.Collections.Generic;
using System.Text;
using AncientWarfare3.core.asyncwork;

namespace AncientWarfare3.core.db
{
    internal static class HistoricalWriteSqlBuilder
    {
        public static HistoricalWriteEnvelope BuildInsert(long sequence,
            string operationKey, string table,
            IReadOnlyList<HistoricalSqlColumn> columns,
            HistoricalWriteKind kind, AWAsyncStamp stamp)
        {
            EnsureIdentifier(table, nameof(table));
            if (columns == null || columns.Count == 0)
                throw new ArgumentException(
                    "Historical insert requires at least one column.",
                    nameof(columns));

            var command = new StringBuilder();
            command.Append("INSERT INTO \"").Append(table).Append("\" (");
            var parameters = new List<HistoricalSqlValue>(columns.Count);
            for (int index = 0; index < columns.Count; index++)
            {
                HistoricalSqlColumn column = columns[index];
                EnsureIdentifier(column.Name, nameof(columns));
                if (index > 0) command.Append(',');
                command.Append('"').Append(column.Name).Append('"');
                parameters.Add(new HistoricalSqlValue(
                    "@p" + index, column.Value));
            }
            command.Append(") VALUES (");
            for (int index = 0; index < columns.Count; index++)
            {
                if (index > 0) command.Append(',');
                command.Append("@p").Append(index);
            }
            command.Append(");");
            return new HistoricalWriteEnvelope(sequence, operationKey,
                command.ToString(), parameters.ToArray(), kind, stamp,
                HistoricalWriteShadowRules.SummarizeInsert(table,
                    operationKey, sequence, columns,
                    pGeneratedEventId: false));
        }

        public static HistoricalWriteEnvelope BuildUpdateThenInsert(
            long sequence, string operationKey, string table,
            IReadOnlyList<HistoricalSqlColumn> keys,
            IReadOnlyList<HistoricalSqlColumn> updates,
            IReadOnlyList<HistoricalSqlColumn> inserts, AWAsyncStamp stamp)
        {
            EnsureIdentifier(table, nameof(table));
            EnsureColumns(keys, nameof(keys));
            EnsureColumns(updates, nameof(updates));
            EnsureColumns(inserts, nameof(inserts));

            var command = new StringBuilder();
            var parameters = new List<HistoricalSqlValue>(
                keys.Count + updates.Count + inserts.Count);
            command.Append("UPDATE \"").Append(table).Append("\" SET ");
            for (int index = 0; index < updates.Count; index++)
            {
                if (index > 0) command.Append(',');
                string parameter = "@u" + index;
                command.Append('"').Append(updates[index].Name)
                    .Append("\"=").Append(parameter);
                parameters.Add(new HistoricalSqlValue(parameter,
                    updates[index].Value));
            }
            command.Append(" WHERE ");
            for (int index = 0; index < keys.Count; index++)
            {
                if (index > 0) command.Append(" AND ");
                string parameter = "@k" + index;
                command.Append('"').Append(keys[index].Name)
                    .Append("\"=").Append(parameter);
                parameters.Add(new HistoricalSqlValue(parameter,
                    keys[index].Value));
            }
            command.Append(";INSERT INTO \"").Append(table).Append("\" (");
            for (int index = 0; index < inserts.Count; index++)
            {
                if (index > 0) command.Append(',');
                command.Append('"').Append(inserts[index].Name).Append('"');
            }
            command.Append(") SELECT ");
            for (int index = 0; index < inserts.Count; index++)
            {
                if (index > 0) command.Append(',');
                string parameter = "@i" + index;
                command.Append(parameter);
                parameters.Add(new HistoricalSqlValue(parameter,
                    inserts[index].Value));
            }
            command.Append(" WHERE changes()=0;");
            return new HistoricalWriteEnvelope(sequence, operationKey,
                command.ToString(), parameters.ToArray(),
                HistoricalWriteKind.State, stamp,
                HistoricalWriteShadowRules.SummarizeState(table,
                    operationKey, sequence, keys, updates, inserts));
        }

        private static void EnsureColumns(
            IReadOnlyList<HistoricalSqlColumn> pColumns,
            string pParameterName)
        {
            if (pColumns == null || pColumns.Count == 0)
                throw new ArgumentException(
                    "Historical state write requires columns.",
                    pParameterName);
            for (int index = 0; index < pColumns.Count; index++)
                EnsureIdentifier(pColumns[index].Name, pParameterName);
        }

        private static void EnsureIdentifier(string pValue,
            string pParameterName)
        {
            if (string.IsNullOrEmpty(pValue) || !IsIdentifierStart(pValue[0]))
                throw new ArgumentException(
                    "Unsafe SQLite identifier.", pParameterName);
            for (int index = 1; index < pValue.Length; index++)
                if (!IsIdentifierPart(pValue[index]))
                    throw new ArgumentException(
                        "Unsafe SQLite identifier.", pParameterName);
        }

        private static bool IsIdentifierStart(char pValue)
        {
            return pValue == '_' || pValue >= 'A' && pValue <= 'Z' ||
                   pValue >= 'a' && pValue <= 'z';
        }

        private static bool IsIdentifierPart(char pValue)
        {
            return IsIdentifierStart(pValue) ||
                   pValue >= '0' && pValue <= '9';
        }
    }
}
