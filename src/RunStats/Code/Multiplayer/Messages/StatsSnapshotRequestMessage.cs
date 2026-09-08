using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace RunStats.Multiplayer.Messages;

public sealed class StatsSnapshotRequestMessage : INetMessage
{
    public int ProtocolVersion { get; set; }

    public long RequestSequence { get; set; }

    public long ClientRevision { get; set; }

    public bool ShouldBroadcast => false;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.VeryDebug;

    public bool ShouldBuffer => true;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(ProtocolVersion);
        writer.WriteLong(RequestSequence);
        writer.WriteLong(ClientRevision);
    }

    public void Deserialize(PacketReader reader)
    {
        ProtocolVersion = reader.ReadInt();
        RequestSequence = reader.ReadLong();
        ClientRevision = reader.ReadLong();
    }
}
