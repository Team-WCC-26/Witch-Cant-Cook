// Implement on client-only stage effects, pollution, event controllers, etc.
// Called once after an authoritative lobby-return confirmation, before player placement.
// Implementations must only reset local state; do not send gameplay packets here.
public interface IClientStageResettable
{
    void ResetClientStage();
}
