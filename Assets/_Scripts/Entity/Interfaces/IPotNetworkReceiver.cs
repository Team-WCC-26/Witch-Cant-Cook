using Protocol;

public interface IPotNetworkReceiver
{
    void HandlePotPlayerEnter(PotPlayerEnterPacket packet);
    void HandlePotEject(PotEjectPacket packet);
    void HandlePotCookComplete(PotCookCompletePacket packet);
}