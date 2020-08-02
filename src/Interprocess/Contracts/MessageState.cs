namespace Cloudtoid.Interprocess
{
    internal enum MessageState : long
    {
        BeingCreated = 0, // do NOT change from zero
        ReadyToBeConsumed = 1,
        LockedToBeConsumed = 2,
    }
}
