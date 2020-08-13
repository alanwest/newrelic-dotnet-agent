using NewRelic.Agent.Api;
using NewRelic.Agent.Extensions.Providers.Wrapper;
using NUnit.Framework;
using System;
using Telerik.JustMock;

namespace NewRelic.Agent.Core.Wrapper
{
    [TestFixture]
    public class Class_WrapperMap
    {
        private static readonly Exception WrapperWorked = new Exception();
        public static readonly AfterWrappedMethodDelegate WrapperWorkedDelegate = (obj, ex) => throw WrapperWorked;

        [Test]
        public void Fancy_Fail()
        {
            try
            {
                WrapperMap.CreateMethodWrappers(Mock.Create<IMethodWrapper>().GetType());
                Assert.Fail("should have thrown");
            }
            catch (Exception ex)
            {
                Assert.AreEqual("Castle.Proxies.IMethodWrapperProxy has an invalid base type", ex.Message);
            }
        }

        [Test]
        public void SimpleWrapper()
        {
            var invokers = WrapperMap.CreateMethodWrappers(typeof(MyWrapperTest));
            Assert.AreEqual(1, invokers.Length);

            var target = this;
            var args = new object[] { "arg0" };
            var instrumentedMethodCall = new InstrumentedMethodCall(new MethodCall(Mock.Create<Method>(), target, args), Mock.Create<InstrumentedMethodInfo>());

            AssertWrapper(() => invokers[0].BeforeWrappedMethod(instrumentedMethodCall, Mock.Create<IAgent>(), Mock.Create<ITransaction>()));
        }

        [Test]
        public void InterfaceWrapper()
        {
            var wrappers = WrapperMap.CreateMethodWrappers(typeof(InterfaceMethodWrapper));
            Assert.AreEqual(1, wrappers.Length);

            var target = new InterfaceTest();
            var instrumentedMethodInfo = new InstrumentedMethodInfo(0, new Method(typeof(IDisposable), "Dispose", ""), "", false, null, TransactionNamePriority.FrameworkHigh, false);

            Assert.IsTrue(wrappers[0].CanWrap(instrumentedMethodInfo).CanWrap);
            var args = new object[] { };
            var instrumentedMethodCall = new InstrumentedMethodCall(new MethodCall(Mock.Create<Method>(), target, args), instrumentedMethodInfo);
            AssertWrapper(() => wrappers[0].BeforeWrappedMethod(instrumentedMethodCall, Mock.Create<IAgent>(), Mock.Create<ITransaction>()));
        }

        private static void AssertWrapper(Func<AfterWrappedMethodDelegate> func)
        {
            try
            {
                func.Invoke().Invoke("dude!", null);
                Assert.Fail();
            }
            catch (Exception ex)
            {
                Assert.AreSame(WrapperWorked, ex);
            }
        }
    }

    public class InterfaceTest : IDisposable
    {
        public void Dispose()
        {
        }
    }


    [ClassWrapper(MatchInterface = true)]
    public class InterfaceMethodWrapper : MethodWrapper<IDisposable>
    {
        public InterfaceMethodWrapper(IAgent agentWrapperApi, ITransaction transaction, InstrumentedMethodCall instrumentedMethodCall, IDisposable invocationTarget) : base(agentWrapperApi, transaction, instrumentedMethodCall, invocationTarget)
        {
        }

        [MethodWrapper]
        public AfterWrappedMethodDelegate Dispose()
        {
            return Class_WrapperMap.WrapperWorkedDelegate;
        }

    }

    public class MyWrapperTest : MethodWrapper<Class_WrapperMap>
    {
        public MyWrapperTest(IAgent agentWrapperApi, ITransaction transaction, InstrumentedMethodCall instrumentedMethodCall, Class_WrapperMap invocationTarget) : base(agentWrapperApi, transaction, instrumentedMethodCall, invocationTarget)
        {
        }

        [MethodWrapper]
        public AfterWrappedMethodDelegate DoThing(string arg0)
        {
            if (InstrumentedMethodCall == null)
                throw new NullReferenceException("InstrumentedMethodCall");
            if (InvocationTarget == null)
                throw new NullReferenceException("target");
            if (arg0 == null)
                throw new NullReferenceException("arg0");
            return Class_WrapperMap.WrapperWorkedDelegate;
        }
    }
}
