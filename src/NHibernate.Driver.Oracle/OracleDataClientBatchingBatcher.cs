using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Text;
using NHibernate.AdoNet;
using NHibernate.AdoNet.Util;
using NHibernate.Exceptions;
#if MANAGED
using Oracle.ManagedDataAccess.Client;
#else
using Oracle.DataAccess.Client;
#endif

#if MANAGED
namespace NHibernate.Driver.OracleManaged
#else
namespace NHibernate.Driver.Oracle
#endif
{
    /// <summary>
    /// Summary description for OracleDataClientBatchingBatcher.
    /// By Tomer Avissar
    /// </summary>
    public class OracleDataClientBatchingBatcher : AbstractBatcher
    {
        private int _batchSize;
        private int _countOfCommands;
        private int _totalExpectedRowsAffected;
        private OracleCommand _currentBatch;
        private IDictionary<string, List<object>> _parameterValueListHashTable;
        private IDictionary<string, bool> _parameterIsAllNullsHashTable;
        private StringBuilder _currentBatchCommandsLog;

        public OracleDataClientBatchingBatcher(ConnectionManager connectionManager, IInterceptor interceptor)
            : base(connectionManager, interceptor)
        {
            _batchSize = Factory.Settings.AdoBatchSize;
            //we always create this, because we need to deal with a scenario in which
            //the user change the logging configuration at runtime. Trying to put this
            //behind an if(log.IsDebugEnabled) will cause a null reference exception 
            //at that point.
            _currentBatchCommandsLog = new StringBuilder().AppendLine("Batch commands:");
        }

        public override void AddToBatch(IExpectation expectation)
        {
            bool firstOnBatch = true;
            _totalExpectedRowsAffected += expectation.ExpectedRowCount;
            string lineWithParameters = null;
            var sqlStatementLogger = Factory.Settings.SqlStatementLogger;
            if (sqlStatementLogger.IsDebugEnabled || Log.IsDebugEnabled)
            {
                lineWithParameters = sqlStatementLogger.GetCommandLineWithParameters(CurrentCommand);
                var formatStyle = sqlStatementLogger.DetermineActualStyle(FormatStyle.Basic);
                lineWithParameters = formatStyle.Formatter.Format(lineWithParameters);
                _currentBatchCommandsLog.Append("command ")
                    .Append(_countOfCommands)
                    .Append(":")
                    .AppendLine(lineWithParameters);
            }
            if (Log.IsDebugEnabled)
            {
                Log.Debug("Adding to batch:" + lineWithParameters);
            }

            if (_currentBatch == null)
            {
                // use first command as the batching command
                _currentBatch = (OracleCommand) CurrentCommand;
                _parameterValueListHashTable = new Dictionary<string, List<object>>();
                //oracle does not allow array containing all null values
                // so this Dictionary is keeping track if all values are null or not
                _parameterIsAllNullsHashTable = new Dictionary<string, bool>();
            }
            else
            {
                firstOnBatch = false;
            }

            foreach (OracleParameter currentParameter in CurrentCommand.Parameters)
            {
                List<object> parameterValueList;
                if (firstOnBatch)
                {
                    parameterValueList = new List<object>();
                    _parameterValueListHashTable.Add(currentParameter.ParameterName, parameterValueList);
                    _parameterIsAllNullsHashTable.Add(currentParameter.ParameterName, true);
                }
                else
                {
                    parameterValueList = _parameterValueListHashTable[currentParameter.ParameterName];
                }

                if (currentParameter.Value != DBNull.Value)
                {
                    _parameterIsAllNullsHashTable[currentParameter.ParameterName] = false;
                }
                parameterValueList.Add(currentParameter.Value);
            }

            _countOfCommands++;

            if (_countOfCommands >= _batchSize)
            {
                ExecuteBatchWithTiming(_currentBatch);
            }
        }

        protected override void DoExecuteBatch(IDbCommand ps)
        {
            if (_currentBatch != null)
            {
                int arraySize = 0;
                _countOfCommands = 0;

                Log.Info("Executing batch");
                CheckReaders();
                Prepare(_currentBatch);

                if (Factory.Settings.SqlStatementLogger.IsDebugEnabled)
                {
                    Factory.Settings.SqlStatementLogger.LogBatchCommand(_currentBatchCommandsLog.ToString());
                    _currentBatchCommandsLog = new StringBuilder().AppendLine("Batch commands:");
                }

                foreach (OracleParameter currentParameter in _currentBatch.Parameters)
                {
                    var parameterValueArray = _parameterValueListHashTable[currentParameter.ParameterName];
                    currentParameter.Value = parameterValueArray.ToArray();
                    arraySize = parameterValueArray.Count;
                }

                _currentBatch.ArrayBindCount = arraySize;
                int rowsAffected;
                try
                {
                    rowsAffected = _currentBatch.ExecuteNonQuery();
                }
                catch (DbException e)
                {
                    throw ADOExceptionHelper.Convert(Factory.SQLExceptionConverter, e, "could not execute batch command.");
                }

                Expectations.VerifyOutcomeBatched(_totalExpectedRowsAffected, rowsAffected);

                _totalExpectedRowsAffected = 0;
                _currentBatch = null;
                _parameterValueListHashTable = null;
            }
        }

        protected override int CountOfStatementsInCurrentBatch
        {
            get { return _countOfCommands; }
        }

        public override int BatchSize
        {
            get { return _batchSize; }
            set { _batchSize = value; }
        }
    }
}