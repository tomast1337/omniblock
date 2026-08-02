namespace BetaSharp.Network.Messages;

[WireMessage("beta:health_update")]
public partial class HealthUpdateMessage : Message
{
    /// <summary>Health in half-hearts, as a short on the wire — matches Beta 1.7.3's range.</summary>
    [WireField]
    public short HealthMp { get; set; }
}
