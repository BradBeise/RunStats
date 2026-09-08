using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace RunStats.Multiplayer.Messages;

public sealed class StatsSnapshotMessage : INetMessage
{
    public StatsSyncSnapshot? Snapshot { get; set; }

    public bool ShouldBroadcast => false;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.VeryDebug;

    public bool ShouldBuffer => true;

    public void Serialize(PacketWriter writer) =>
        StatsSnapshotPacketCodec.Write(writer, Snapshot!);

    public void Deserialize(PacketReader reader) =>
        Snapshot = StatsSnapshotPacketCodec.Read(reader);
}
