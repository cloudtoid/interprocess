using System;

namespace Cloudtoid.Interprocess.Tests
{
    [Flags]
    public enum Platform
    {
        Windows = 0x01,
        Linux = 0x02,
        OSX = 0x04,
        FreeBSD = 0x08,

        UnixBased = Linux | OSX | FreeBSD,
        All = Windows | Linux | OSX | FreeBSD
    }
}