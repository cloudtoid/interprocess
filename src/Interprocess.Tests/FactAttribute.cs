using System;
using System.Runtime.InteropServices;

namespace Cloudtoid.Interprocess.Tests
{
    public class FactAttribute : Xunit.FactAttribute
    {
        private static readonly Platform? CurrentPlatform = GetPlatform();

        /// <summary>
        /// Gets or sets the supported OS Platforms
        /// </summary>
        public Platform Platforms { get; set; } = Platform.All;

        public override string? Skip
        {
            get
            {
                if (base.Skip != null || CurrentPlatform is null)
                    return base.Skip;

                if ((Platforms & CurrentPlatform) == 0)
                    return $"Skipped on {CurrentPlatform}";

                return null;
            }
            set => base.Skip = value;
        }

        private static Platform? GetPlatform()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Platform.Windows;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return Platform.Linux;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return Platform.OSX;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD))
                return Platform.FreeBSD;

            return null;
        }
    }

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