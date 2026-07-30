using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace AncientWarfare3.core.db
{
    internal interface IHistoricalCustomWriteEnvelope
    {
        object Execute(SQLiteConnection pConnection,
            SQLiteTransaction pTransaction);
    }

    internal sealed class HistoricalSqliteBatchSink : IHistoricalWriteBatchSink
    {
        private readonly string _databasePath;
        private readonly long _databaseEpoch;
        private readonly Func<long> _currentDatabaseEpoch;
        private SQLiteConnection _connection;

        public HistoricalSqliteBatchSink(string pDatabasePath,
            long pDatabaseEpoch, Func<long> pCurrentDatabaseEpoch)
        {
            _databasePath = pDatabasePath ?? string.Empty;
            _databaseEpoch = pDatabaseEpoch;
            _currentDatabaseEpoch = pCurrentDatabaseEpoch ??
                throw new ArgumentNullException(nameof(pCurrentDatabaseEpoch));
        }

        public void Open()
        {
            EnsureCurrentEpoch();
            var builder = new SQLiteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Version = 3,
                Pooling = false
            };
            _connection = new SQLiteConnection(builder.ConnectionString);
            _connection.Open();
            LineageArchivePragmaService.Configure(_connection);
            EnsureCurrentEpoch();
        }

        public HistoricalWriteBatchResult Execute(
            IReadOnlyList<HistoricalWriteEnvelope> pOperations)
        {
            if (_connection == null)
                return HistoricalWriteBatchResult.Terminal(
                    "historical SQLite connection is unavailable");
            if (pOperations == null || pOperations.Count == 0)
                return HistoricalWriteBatchResult.Committed();

            SQLiteTransaction transaction = null;
            try
            {
                EnsureCurrentEpoch();
                transaction = _connection.BeginTransaction();
                var outcomes = new object[pOperations.Count];
                for (int index = 0; index < pOperations.Count; index++)
                {
                    HistoricalWriteEnvelope operation = pOperations[index];
                    if (operation is IHistoricalCustomWriteEnvelope custom)
                    {
                        outcomes[index] = custom.Execute(_connection,
                            transaction);
                        continue;
                    }
                    using var command = new SQLiteCommand(_connection)
                    {
                        Transaction = transaction,
                        CommandText = operation.CommandText
                    };
                    foreach (HistoricalSqlValue parameter in operation.Parameters)
                        command.Parameters.AddWithValue(parameter.Name,
                            parameter.Value ?? DBNull.Value);
                    command.ExecuteNonQuery();
                }
                EnsureCurrentEpoch();
                transaction.Commit();
                transaction = null;
                return HistoricalWriteBatchResult.Committed(outcomes);
            }
            catch (Exception error)
            {
                try { transaction?.Rollback(); }
                catch { }
                return IsRetryable(error)
                    ? HistoricalWriteBatchResult.Retryable(error.Message)
                    : HistoricalWriteBatchResult.Terminal(error.Message);
            }
            finally
            {
                transaction?.Dispose();
            }
        }

        public void Dispose()
        {
            try { _connection?.Close(); }
            finally
            {
                _connection?.Dispose();
                _connection = null;
            }
        }

        private void EnsureCurrentEpoch()
        {
            if (_currentDatabaseEpoch() != _databaseEpoch)
                throw new InvalidOperationException(
                    "Lineage archive database epoch changed.");
        }

        private static bool IsRetryable(Exception pError)
        {
            for (Exception current = pError; current != null;
                 current = current.InnerException)
            {
                if (current is SQLiteException sqlite &&
                    HistoricalWriteFailureRules.IsRetryableSqliteErrorCode(
                        (int)sqlite.ResultCode))
                    return true;
                if (HistoricalWriteFailureRules.IsRetryableSqliteMessage(
                        current.Message))
                    return true;
            }
            return false;
        }
    }
}
