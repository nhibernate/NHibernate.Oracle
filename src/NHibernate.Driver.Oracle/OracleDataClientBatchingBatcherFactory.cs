using NHibernate.AdoNet;
using NHibernate.Engine;

#if MANAGED
namespace NHibernate.Driver.OracleManaged
#else
namespace NHibernate.Driver.Oracle
#endif
{
    public class OracleDataClientBatchingBatcherFactory : IBatcherFactory
    {
        public virtual IBatcher CreateBatcher(ConnectionManager connectionManager, IInterceptor interceptor)
        {
            return new OracleDataClientBatchingBatcher(connectionManager, interceptor);
        }
    }
}