using MongoDB.Driver;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

/// <summary>Provides the client belonging to the lifetime owner for effective connection settings.</summary>
public interface IMongoClientPool
{
    /// <summary>Gets an existing client or creates one for the supplied effective settings.</summary>
    /// <param name="settings">Settings resolved for the captured operation.</param>
    /// <returns>The client owned by the pool; callers do not dispose it.</returns>
    IMongoClient GetClient(MongoClientSettings settings);
}
