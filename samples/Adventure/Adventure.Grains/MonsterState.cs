using Adventure.GrainInterfaces;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Timers;

namespace Adventure.Grains;

public sealed class MonsterState
{
    public long Id { get; set; }
    public MonsterInfo? Info { get; set; }
    public IRoomGrain? Room { get; set; }
    public IGrainTimer? Timer { get; set; }
    public IGrainFactory? Factory { get; set; }
}