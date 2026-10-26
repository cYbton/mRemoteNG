using System.Data;
using mRemoteNG.Config.DatabaseConnectors;
using mRemoteNG.Messages;
using mRemoteNG.App;
using System.Runtime.Versioning;

namespace mRemoteNG.Config.DataProviders
{
    [SupportedOSPlatform("windows")]
    public class SqlDataProvider(IDatabaseConnector databaseConnector) : IDataProvider<DataTable>
    {
        public IDatabaseConnector DatabaseConnector { get; } = databaseConnector;

        public DataTable Load()
        {
            DataTable dataTable = new();
            System.Data.Common.DbCommand dbQuery = DatabaseConnector.DbCommand("SELECT * FROM tblCons ORDER BY PositionID ASC");
            DatabaseConnector.AssociateItemToThisConnector(dbQuery);
            if (!DatabaseConnector.IsConnected)
                OpenConnection();
            System.Data.Common.DbDataReader dbDataReader = dbQuery.ExecuteReader(CommandBehavior.CloseConnection);

            if (dbDataReader.HasRows)
                dataTable.Load(dbDataReader);
            dbDataReader.Close();
            return dataTable;
        }

        public void Save(DataTable dataTable)
        {
            if (DbUserIsReadOnly())
            {
                Runtime.MessageCollector.AddMessage(MessageClass.InformationMsg, "Trying to save connections but the SQL read only checkbox is checked, aborting!");
                return;
            }

            if (!DatabaseConnector.IsConnected)
                OpenConnection();
            // sysprep fork: no SQL Server/MySQL driver in this build (see MSSqlDatabaseConnector)
            throw new System.NotSupportedException("Storing the connections in a database is not part of the sysprep.io build of mRemoteNG.");
        }

        public void OpenConnection()
        {
            DatabaseConnector.Connect();
        }

        public void CloseConnection()
        {
            DatabaseConnector.Disconnect();
        }

        private bool DbUserIsReadOnly()
        {
            return Properties.OptionsDBsPage.Default.SQLReadOnly;
        }
    }
}