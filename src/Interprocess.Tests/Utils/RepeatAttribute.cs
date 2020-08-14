using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit.Sdk;

namespace Cloudtoid.Interprocess.Tests
{
    public sealed class RepeatAttribute : DataAttribute
    {
        private readonly int count;

        public RepeatAttribute(int count)
        {
            this.count = count;
        }

        public override IEnumerable<object[]> GetData(MethodInfo testMethod)
        {
            return Enumerable
                .Range(1, count)
                .Select(i => new object[] { i });
        }
    }
}
