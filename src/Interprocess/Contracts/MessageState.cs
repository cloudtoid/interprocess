namespace Cloudtoid.Interprocess
{
    internal enum MessageState : long
    {
        BeingCreated = 0,
        ReadyToBeConsumed = 1,
        LockedToBeConsumed = 2,
    }
}
