using System;
using System.Data.Common;
using System.Threading.Tasks;

namespace mRemoteNG.Config.DatabaseConnectors
{
    // sysprep fork: storing the connections in SQL Server is not part of this build - the database driver is gone (it
    // made up a large part of the download). The class stays because the SQL code checks for this connector type.
    public class MSSqlDatabaseConnector : IDatabaseConnector
    {
        public MSSqlDatabaseConnector(string server, string catalog, string username, string password)
        {
            throw new NotSupportedException("Storing the connections in SQL Server is not part of the sysprep.io build of mRemoteNG.");
        }

        public DbConnection DbConnection() => throw new NotSupportedException();

        public DbCommand DbCommand(string dbCommand) => throw new NotSupportedException();

        public bool IsConnected => false;

        public void Connect() => throw new NotSupportedException();

        public Task ConnectAsync() => throw new NotSupportedException();

        public void Disconnect()
        {
        }

        public void AssociateItemToThisConnector(DbCommand dbCommand) => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}