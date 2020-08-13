/*
* Copyright 2020 New Relic Corporation. All rights reserved.
* SPDX-License-Identifier: Apache-2.0
*/
using NewRelic.Agent.Api;
using NewRelic.Agent.Core.Utilities;
using NewRelic.Agent.Extensions.Providers.Wrapper;
using NewRelic.Core.Logging;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Versioning;

namespace NewRelic.Agent.Core.Wrapper
{
    /// <summary>
    /// A factory that returns wrappers for instrumented methods
    /// </summary>
    public interface IWrapperMap
    {
        /// <summary>
        /// Return a tracked wrapper that CanWrap the given method.
        /// </summary>
        TrackedWrapper Get(InstrumentedMethodInfo instrumentedMethodInfo);

        /// <summary>
        /// Returns the NoOp wrapper.
        /// </summary>
        /// <param name="instrumentedMethodInfo"></param>
        TrackedWrapper GetNoOpWrapper();
    }

    public class WrapperMap : IWrapperMap
    {
        private readonly List<IDefaultWrapper> _defaultWrappers;
        private readonly List<IWrapper> _nonDefaultWrappers;
        private readonly TrackedWrapper _noOpTrackedWrapper;

        public WrapperMap(IAgent agent, IEnumerable<IWrapper> wrappers, MethodWrapperTypes methodWrapperTypes, IDefaultWrapper defaultWrapper, INoOpWrapper noOpWrapper)
        {
            _nonDefaultWrappers = wrappers
                .Where(wrapper => wrapper != null)
                .Where(wrapper => !(wrapper is IDefaultWrapper) && !(wrapper is INoOpWrapper))
                .ToList();

            _nonDefaultWrappers.Add(new AttachToAsyncWrapper());
            _nonDefaultWrappers.Add(new DetachWrapper());
            _nonDefaultWrappers.Add(new CustomSegmentWrapper());
            _nonDefaultWrappers.Add(new IgnoreTransactionWrapper());
            _nonDefaultWrappers.Add(new MultithreadedTrackingWrapper());
            _nonDefaultWrappers.Add(new OtherTransactionWrapper());

            // This allows instrumentation that does nothing other than to track the library version.
            _nonDefaultWrappers.Add(noOpWrapper);

            List<IWrapper> allWrappers = new List<IWrapper>();
            foreach (var wrapperType in methodWrapperTypes.Types)
            {
                try
                {
                    var invokers = CreateMethodWrappers(wrapperType);
                    allWrappers.AddRange(invokers);
                }
                catch (Exception ex)
                {
                    Log.Error(ex.Message);
                    Log.Debug(ex);
                }
            }

            foreach (var wrapper in wrappers)
            {
                if (!(wrapper is IDefaultWrapper || wrapper is INoOpWrapper))
                {
                    allWrappers.Add(wrapper);
                }
            }
            _nonDefaultWrappers = allWrappers;

            var defaultWrappers = new List<IDefaultWrapper> { defaultWrapper, new DefaultWrapperAsync() };

            _defaultWrappers = defaultWrappers;

            _noOpTrackedWrapper = new TrackedWrapper(noOpWrapper);

            if (wrappers.Count() == 0)
            {
                Log.Error("No wrappers were loaded.  The agent will not behave as expected.");
            }

            if (Log.IsFinestEnabled)
            {
                Log.Finest($"WrapperMap has NonDefaultWrappers: {string.Join(", ", _nonDefaultWrappers)}");
            }
        }

        public TrackedWrapper Get(InstrumentedMethodInfo instrumentedMethodInfo)
        {
            //Then, see if there's a standard wrapper supporting this method
            foreach (var wrapper in _nonDefaultWrappers)
            {
                if (CanWrap(instrumentedMethodInfo, wrapper))
                {
                    return new TrackedWrapper(wrapper);
                }
            }

            //Next, check to see if one of the dynamic wrappers can be used
            foreach (var wrapper in ExtensionsLoader.TryGetDynamicWrapperInstance(instrumentedMethodInfo.RequestedWrapperName))
            {
                if (CanWrap(instrumentedMethodInfo, wrapper))
                {
                    return new TrackedWrapper(wrapper);
                }
            }

            //Otherwise, return one of our defaults or a NoOp
            return GetDefaultWrapperOrSetNoOp(instrumentedMethodInfo);
        }

        public TrackedWrapper GetNoOpWrapper()
        {
            return _noOpTrackedWrapper;
        }

        private TrackedWrapper GetDefaultWrapperOrSetNoOp(InstrumentedMethodInfo instrumentedMethodInfo)
        {
            foreach (var wrapper in _defaultWrappers)
            {
                if (CanWrap(instrumentedMethodInfo, wrapper))
                {
                    return new TrackedWrapper(wrapper);
                }
            }

            Log.DebugFormat(
                "No matching wrapper found for {0}.{1}({2}) in assembly [{3}] (requested wrapper name was {4}). This usually indicates misconfigured instrumentation. This method will be ignored.",
                instrumentedMethodInfo.Method.Type.FullName,
                instrumentedMethodInfo.Method.MethodName,
                instrumentedMethodInfo.Method.ParameterTypeNames,
                instrumentedMethodInfo.Method.Type.Assembly.FullName,
                instrumentedMethodInfo.RequestedWrapperName);

            return GetNoOpWrapper();
        }

        private static bool CanWrap(InstrumentedMethodInfo instrumentedMethodInfo, IWrapper wrapper)
        {
            var method = instrumentedMethodInfo.Method;
            var canWrapResponse = wrapper.CanWrap(instrumentedMethodInfo);

            if (canWrapResponse.AdditionalInformation != null && !canWrapResponse.CanWrap)
                Log.Warn(canWrapResponse.AdditionalInformation);
            if (canWrapResponse.AdditionalInformation != null && canWrapResponse.CanWrap)
                Log.Info(canWrapResponse.AdditionalInformation);

            if (canWrapResponse.CanWrap)
                Log.Debug($"Wrapper \"{wrapper.GetType().FullName}\" will be used for instrumented method \"{method.Type}.{method.MethodName}\"");

            return canWrapResponse.CanWrap;
        }

        private delegate IMethodWrapper WrapperFactory(IAgent agentWrapperApi, ITransaction transaction, InstrumentedMethodCall instrumentedMethodCall, object invocationTarget);

        public static IWrapper[] CreateMethodWrappers(Type wrapperType)
        {
            List<IWrapper> invokers = new List<IWrapper>();
            var baseType = wrapperType.BaseType;
            var genericArgs = baseType.GetGenericArguments();
            if (genericArgs.Length != 1)
            {
                throw new Exception(wrapperType.FullName + " has an invalid base type");
            }
            Type invocationTargetType = genericArgs[0];
            WrapperFactory wrapperFactory = CreateWrapperFactory(wrapperType, invocationTargetType);
            // verify that the wrapper factory doesn't blow up when invoked
            try
            {
                wrapperFactory.Invoke(null, null, null, null);
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                return new IWrapper[0];
            }

            foreach (var method in wrapperType.GetMethods())
            {
                var attributes = method.GetCustomAttributes(typeof(MethodWrapperAttribute), false);
                if (attributes.Length > 0)
                {
                    var wrapperInfo = attributes[0] as MethodWrapperAttribute;

                    if (!typeof(AfterWrappedMethodDelegate).IsAssignableFrom(method.ReturnType))
                    {
                        throw new Exception(wrapperType.FullName + '.' + method.Name + " does not return a " + typeof(AfterWrappedMethodDelegate).Name);
                    }

                    var classAttr = wrapperType.GetCustomAttribute(typeof(ClassWrapperAttribute), false) as ClassWrapperAttribute;

                    var parameters = method.GetParameters();
                    List<Type> parameterTypes = new List<Type>();
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        parameterTypes.Add(parameters[i].ParameterType);
                    }

                    MethodInvocationHandler handler = CreateHandler(method);
                    var wrapper = new MethodWrapperBase(wrapperFactory, method.Name, classAttr, wrapperInfo, invocationTargetType, parameterTypes.ToArray(), handler);
                    invokers.Add(wrapper);
                }
            }

            if (invokers.Count == 0)
            {
                throw new Exception("Wrapper " + wrapperType.FullName + " did not define any MethodWrappers");
            }

            return invokers.ToArray();
        }

        private static WrapperFactory CreateWrapperFactory(Type wrapperType, Type invocationTargetType)
        {
            var constructor = wrapperType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, new Type[] { typeof(IAgent), typeof(ITransaction), typeof(InstrumentedMethodCall), invocationTargetType }, null);

            DynamicMethod newMethod = new DynamicMethod("methodWrapperConstructor",
                typeof(IMethodWrapper),
                new Type[] { typeof(IAgent), typeof(ITransaction), typeof(InstrumentedMethodCall), typeof(object) }, wrapperType);

            ILGenerator il = newMethod.GetILGenerator(256);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Castclass, invocationTargetType);
            il.Emit(OpCodes.Newobj, constructor);
            il.Emit(OpCodes.Ret);

            return (WrapperFactory)newMethod.CreateDelegate(typeof(WrapperFactory));
        }

        private delegate AfterWrappedMethodDelegate MethodInvocationHandler(IMethodWrapper wrapper, object[] parameters);

        /// <summary>
        /// Returns a MethodInvocationHandler delegate that can invoke a method marked with
        /// MethodWrapperAttribute.  For example, if method wrapper wraps a method with 2 parameters,
        /// a string and an int, the newly created handler will invoke it by casting arg[0] to a string
        /// and arg[1] to an int.
        /// </summary>
        /// <param name="method"></param>
        /// <returns></returns>
        private static MethodInvocationHandler CreateHandler(MethodInfo method)
        {
            DynamicMethod newMethod = new DynamicMethod("methodWrapperInvoker",
                typeof(AfterWrappedMethodDelegate),
                new Type[] { typeof(IMethodWrapper), typeof(object[]) }, method.DeclaringType);

            var parameters = method.GetParameters();

            ILGenerator il = newMethod.GetILGenerator(256);

            // load the wrapper on the call stack
            il.Emit(OpCodes.Ldarg_0);

            for (int index = 0; index < parameters.Length; index++)
            {
                // load the array
                il.Emit(OpCodes.Ldarg_1);
                // push the array index
                il.Emit(OpCodes.Ldc_I4, index);
                // load the array item as an object
                il.Emit(OpCodes.Ldelem, typeof(object));
                // cast the argument to the expected type
                il.Emit(OpCodes.Castclass, parameters[index].ParameterType);
            }

            il.EmitCall(OpCodes.Callvirt, method, null);
            il.Emit(OpCodes.Ret);

            return (MethodInvocationHandler)newMethod.CreateDelegate(typeof(MethodInvocationHandler));
        }

        private sealed class MethodWrapperBase : IWrapper
        {
            private static String AsyncTransactionsMissingSupportUrl =
    "https://docs.newrelic.com/docs/agents/net-agent/troubleshooting/missing-async-metrics";

            private readonly string _methodName;
            private readonly Type[] _parameterTypes;
            private readonly Type _invocationTargetType;
            private readonly MethodInvocationHandler _handler;
            private readonly WrapperFactory _wrapperFactory;
            private readonly MethodWrapperAttribute _wrapperAttribute;
            private readonly ClassWrapperAttribute _classWrapperAttribute;

            public bool IsTransactionRequired => _wrapperAttribute.TransactionRequired;

            public MethodWrapperBase(WrapperFactory wrapperFactory, string methodName, ClassWrapperAttribute classWrapperAttribute, MethodWrapperAttribute wrapperAttribute, Type invocationTargetType, Type[] parameterTypes, MethodInvocationHandler handler)
            {
                _wrapperFactory = wrapperFactory;
                _methodName = methodName;
                _parameterTypes = parameterTypes;
                _handler = handler;
                _invocationTargetType = invocationTargetType;
                _classWrapperAttribute = classWrapperAttribute ?? new ClassWrapperAttribute();
                _wrapperAttribute = wrapperAttribute;
            }

            public AfterWrappedMethodDelegate BeforeWrappedMethod(InstrumentedMethodCall instrumentedMethodCall, IAgent agent, ITransaction transaction)
            {
                var wrapper = _wrapperFactory.Invoke(agent, transaction, instrumentedMethodCall, instrumentedMethodCall.MethodCall.InvocationTarget);
                return _handler(wrapper, instrumentedMethodCall.MethodCall.MethodArguments);
            }

            public CanWrapResponse CanWrap(InstrumentedMethodInfo instrumentedMethodInfo)
            {
                if (_wrapperAttribute.UsesContinuation)
                {
                    if (LegacyAspPipelineIsPresent())
                    {
                        return new CanWrapResponse(false, LegacyAspPipelineNotSupportedMessage(instrumentedMethodInfo.Method.Type.Assembly.FullName, instrumentedMethodInfo.Method.Type.FullName, instrumentedMethodInfo.Method.MethodName));
                    }
                }

                bool typeMatches = _classWrapperAttribute.MatchInterface ?
                    _invocationTargetType.IsAssignableFrom(instrumentedMethodInfo.Method.Type) :
                    instrumentedMethodInfo.Method.Type.Equals(_invocationTargetType);

                if (!typeMatches)
                {
                    return new CanWrapResponse(false);
                }
                if (!instrumentedMethodInfo.Method.MethodName.Equals(_methodName))
                {
                    return new CanWrapResponse(false);
                }
                if (instrumentedMethodInfo.Method.Parameters.Length != _parameterTypes.Length)
                {
                    return new CanWrapResponse(false);
                }
                for (int i = 0; i < _parameterTypes.Length; i++)
                {
                    if (!instrumentedMethodInfo.Method.Parameters[i].ParameterType.Equals(_parameterTypes[i]))
                    {
                        return new CanWrapResponse(false);
                    }
                }
                return new CanWrapResponse(true);
            }

            private static String LegacyAspPipelineNotSupportedMessage(String assemblyName, String typeName, String methodName)
            {
                return $"The method {methodName} in class {typeName} from assembly {assemblyName} will not be instrumented.  Some async instrumentation is not supported on .NET 4.5 and greater unless you change your application configuration to use the new ASP pipeline. For details see: {AsyncTransactionsMissingSupportUrl}";
            }

            private static Boolean LegacyAspPipelineIsPresent()
            {

#if NETSTANDARD2_0
                return false;
#else
                // first check that the application is even running under ASP.NET
                if (!System.Web.Hosting.HostingEnvironment.IsHosted)
                {
                    return false;
                }

                // This will return true if the web.config includes <httpRuntime targetFramework="4.5">
                var targetFrameworkName = AppDomain.CurrentDomain.GetData("ASPNET_TARGETFRAMEWORK") as FrameworkName;
                if (targetFrameworkName?.Version >= new Version(4, 5))
                {
                    return false;
                }

                // This will return true if the web.config includes <add key="aspnet:UseTaskFriendlySynchronizationContext" value="true" />
                Boolean isTaskFriendlySyncContextEnabled;
                var appSettingValue = ConfigurationManager.AppSettings["aspnet:UseTaskFriendlySynchronizationContext"];
                Boolean.TryParse(appSettingValue, out isTaskFriendlySyncContextEnabled);

                return !isTaskFriendlySyncContextEnabled;
#endif
            }
        }
    }
}
