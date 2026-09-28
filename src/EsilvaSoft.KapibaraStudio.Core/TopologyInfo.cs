namespace EsilvaSoft.KapibaraStudio.Core;

public sealed record TopologyInfo(string Kind, string? ReplicaSet, string Server, IReadOnlyList<InstanceInfo> Instances, string Definition);
