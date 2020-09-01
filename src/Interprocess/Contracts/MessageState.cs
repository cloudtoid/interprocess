namespace Cloudtoid.Interprocess
{
    internal enum MessageState : int
    {
        BeingCreated = 0, // do NOT change from zero
        ReadyToBeConsumed = 1,
        LockedToBeConsumed = 2,
    }
}
