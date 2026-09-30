using Protocol;

public interface IPotNetworkReceiver
{
    void HandlePotPlayerEnter(ToolPlayerEnterPacket packet);
    void HandlePotEject(PotEjectPacket packet);
    void HandlePotCookComplete(PotCookCompletePacket packet);
}