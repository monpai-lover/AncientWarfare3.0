using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using AncientWarfare3.core.asyncwork;

namespace AncientWarfare3.core.db
{
    internal enum HistoricalWriteKind
    {
        Append,
        State
    }

    internal readonly struct HistoricalSqlColumn
    {
        private readonly HistoricalSqlValue _value;

        public HistoricalSqlColumn(string pName, object pValue)
        {
            Name = pName ?? string.Empty;
            _value = new HistoricalSqlValue("@value", pValue);
        }

        public string Name { get; }
        public object Value => _value.Value;
    }

    internal readonly struct HistoricalSqlValue
    {
        private readonly object _value;

        public HistoricalSqlValue(string pName, object pValue)
        {
            if (string.IsNullOrEmpty(pName) || pName[0] != '@')
                throw new ArgumentException(
                    "SQL parameter names must start with @.", nameof(pName));
            if (!IsSupported(pValue))
                throw new ArgumentException(
                    "Unsupported historical SQL parameter type.",
                    nameof(pValue));
            Name = pName;
            _value = CloneBytes(pValue);
        }

        public string Name { get; }
        public object Value => CloneBytes(_value);

        private static bool IsSupported(object pValue)
        {
            return pValue == null || pValue == DBNull.Value ||
                   pValue is byte || pValue is short || pValue is int ||
                   pValue is long || pValue is float || pValue is double ||
                   pValue is bool || pValue is string || pValue is byte[];
        }

        private static object CloneBytes(object pValue)
        {
            return pValue is byte[] bytes ? (byte[])bytes.Clone() : pValue;
        }
    }

    internal static class HistoricalWriteShadowRules
    {
        public static string SummarizeInsert(string pTable,
            string pOperationKey, long pSequence,
            IReadOnlyList<HistoricalSqlColumn> pColumns,
            bool pGeneratedEventId)
        {
            var result = Header("Append", pTable, pOperationKey, pSequence);
            AppendColumns(result, "fields", pColumns, pGeneratedEventId);
            return result.ToString();
        }

        public static string SummarizeState(string pTable,
            string pOperationKey, long pSequence,
            IReadOnlyList<HistoricalSqlColumn> pKeys,
            IReadOnlyList<HistoricalSqlColumn> pUpdates,
            IReadOnlyList<HistoricalSqlColumn> pInserts)
        {
            var result = Header("State", pTable, pOperationKey, pSequence);
            AppendColumns(result, "keys", pKeys, pGeneratedEventId: false);
            AppendColumns(result, "updates", pUpdates,
                pGeneratedEventId: false);
            AppendColumns(result, "inserts", pInserts,
                pGeneratedEventId: false);
            return result.ToString();
        }

        private static StringBuilder Header(string pKind, string pTable,
            string pOperationKey, long pSequence)
        {
            return new StringBuilder().Append("kind=").Append(pKind)
                .Append(",table=").Append(pTable ?? string.Empty)
                .Append(",key=").Append(pOperationKey ?? string.Empty)
                .Append(",sequence=").Append(pSequence);
        }

        private static void AppendColumns(StringBuilder pResult,
            string pSection, IReadOnlyList<HistoricalSqlColumn> pColumns,
            bool pGeneratedEventId)
        {
            pResult.Append(',').Append(pSection).Append('=');
            if (pColumns == null || pColumns.Count == 0)
            {
                pResult.Append("none");
                return;
            }
            for (int index = 0; index < pColumns.Count; index++)
            {
                if (index > 0) pResult.Append('|');
                HistoricalSqlColumn column = pColumns[index];
                pResult.Append(column.Name).Append('=');
                if (pGeneratedEventId && string.Equals(column.Name,
                        "EVENT_ID", StringComparison.Ordinal))
                    pResult.Append("<generated>");
                else
                    AppendValue(pResult, column.Value);
            }
        }

        private static void AppendValue(StringBuilder pResult, object pValue)
        {
            if (pValue == null)
            {
                pResult.Append("null");
                return;
            }
            if (pValue == DBNull.Value)
            {
                pResult.Append("dbnull");
                return;
            }
            if (pValue is string text)
            {
                pResult.Append("string:").Append(text.Length).Append(':')
                    .Append(HashText(text).ToString("X16",
                        CultureInfo.InvariantCulture));
                return;
            }
            if (pValue is byte[] bytes)
            {
                pResult.Append("bytes:").Append(bytes.Length).Append(':')
                    .Append(HashBytes(bytes).ToString("X16",
                        CultureInfo.InvariantCulture));
                return;
            }
            if (pValue is bool boolean)
            {
                pResult.Append(boolean ? "true" : "false");
                return;
            }
            if (pValue is IFormattable formattable)
                pResult.Append(formattable.ToString(null,
                    CultureInfo.InvariantCulture));
            else
                pResult.Append(pValue);
        }

        private static ulong HashText(string pValue)
        {
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                for (int index = 0; index < pValue.Length; index++)
                {
                    hash ^= pValue[index];
                    hash *= 1099511628211UL;
                }
                return hash;
            }
        }

        private static ulong HashBytes(byte[] pValue)
        {
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                for (int index = 0; index < pValue.Length; index++)
                {
                    hash ^= pValue[index];
                    hash *= 1099511628211UL;
                }
                return hash;
            }
        }
    }

    internal class HistoricalWriteEnvelope
    {
        private readonly ReadOnlyCollection<HistoricalSqlValue> _parameters;

        public HistoricalWriteEnvelope(long pSequence, string pOperationKey,
            string pCommandText, HistoricalSqlValue[] pParameters,
            HistoricalWriteKind pKind, AWAsyncStamp pStamp,
            string pShadowSummary = null)
        {
            Sequence = pSequence;
            OperationKey = pOperationKey ?? string.Empty;
            CommandText = pCommandText ?? string.Empty;
            HistoricalSqlValue[] copied = pParameters == null
                ? Array.Empty<HistoricalSqlValue>()
                : (HistoricalSqlValue[])pParameters.Clone();
            _parameters = Array.AsReadOnly(copied);
            Kind = pKind;
            Stamp = pStamp;
            ShadowSummary = pShadowSummary ?? string.Empty;
        }

        public long Sequence { get; }
        public string OperationKey { get; }
        public string CommandText { get; }
        public IReadOnlyList<HistoricalSqlValue> Parameters => _parameters;
        public HistoricalWriteKind Kind { get; }
        public AWAsyncStamp Stamp { get; }
        public string ShadowSummary { get; }
    }
}
