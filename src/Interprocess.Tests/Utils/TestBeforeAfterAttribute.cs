using System;
using System.Reflection;
using Xunit.Sdk;

namespace Cloudtoid.Interprocess.Tests
{
    public class TestBeforeAfterAttribute : BeforeAfterTestAttribute
    {
        public override void Before(MethodInfo methodUnderTest)
        {
            Console.WriteLine("Before - " + methodUnderTest.Name);
        }

        public override void After(MethodInfo methodUnderTest)
        {
            Console.WriteLine("After - " + methodUnderTest.Name);
        }
    }
}