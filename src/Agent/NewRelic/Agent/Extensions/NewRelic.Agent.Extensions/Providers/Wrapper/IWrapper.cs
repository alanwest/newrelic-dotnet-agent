/*
* Copyright 2020 New Relic Corporation. All rights reserved.
* SPDX-License-Identifier: Apache-2.0
*/
using System;
using System.Collections.Generic;
using NewRelic.Agent.Api;

namespace NewRelic.Agent.Extensions.Providers.Wrapper
{
    /// <summary>
    /// This option attribute can be used on a MethodWrapper to indicate that it should
    /// make the implementations of an interface instead of a specific class.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class ClassWrapperAttribute : Attribute
    {
        public bool MatchInterface = false;
    }

    /// <summary>
    /// This attribute marks methods in a MethodWrapper that are meant to intercept (wrap)
    /// a method on the targeted class.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class MethodWrapperAttribute : Attribute
    {
        public bool TransactionRequired = true;
        public bool UsesContinuation = false;
    }

    /// <summary>
    /// A method wrapper targets either a specific class or implementations of an interface.
    /// An instance of the method wrapper will be instantiated for each instrumented method 
    /// invocation which it handles.
    /// </summary>
    /// <typeparam name="I">The invocation target type.</typeparam>
    public abstract class MethodWrapper<I> : IMethodWrapper
    {
        protected I InvocationTarget { get; }
        protected ITransaction Transaction { get; }
        protected InstrumentedMethodCall InstrumentedMethodCall { get; }
        protected IAgent Agent { get; }

        public MethodWrapper(IAgent agent, ITransaction transaction, InstrumentedMethodCall instrumentedMethodCall, I invocationTarget)
        {
            Agent = agent;
            Transaction = transaction;
            InstrumentedMethodCall = instrumentedMethodCall;
            InvocationTarget = invocationTarget;
        }
    }

    public interface IWrapper
    {
        /// <summary>
        /// Called once per method per AppDomain to determine whether or not this wrapper can wrap the given instrumented method info. Returns a response struct that contains a boolean and a string. The boolean is true if the provided method is one that this wrapper knows how to handle, false otherwise. The string optionally contains null or any additional information that may explain why the boolean was true or false.
        /// </summary>
        /// <param name="instrumentedMethodInfo">Details about the method and wrapper that is being wrapped.</param>
        /// <returns>A response struct that contains a boolean and a string. The boolean is true if the provided method is one that this wrapper knows how to handle, false otherwise. The string optionally contains null or any additional information that may explain why the boolean was true or false.</returns>
        CanWrapResponse CanWrap(InstrumentedMethodInfo instrumentedMethodInfo);

        /// <summary>
        /// Performs work before a wrapped method call and returns a delegate containing work to perform after the wrapped method call.
        /// </summary>
        /// <param name="instrumentedMethodCall">The method call being wrapped, plus any instrumentation options.</param>
        /// <param name="agent">The API that wrappers can use to talk to the agent.</param>
        /// <param name="transaction">The current transaction or null if IsTransactionRequired is false</param>
        AfterWrappedMethodDelegate BeforeWrappedMethod(InstrumentedMethodCall instrumentedMethodCall, IAgent agent, ITransaction transaction);

        /// <summary>
        /// Returns true if this wrapper requires a transaction.  If it does, BeforeWrappedMethod will not be invoked
        /// when a wrapper is requested and there is no current transaction.
        /// </summary>
        /// <returns></returns>
        bool IsTransactionRequired { get; }
    }

    /// <summary>
    /// Wrappers should not directly reference this interface.  Use the MethodWrapper class instead.
    /// </summary>
    public interface IMethodWrapper { }

    /// <summary>
    /// A container of classes that extend the MethodWrapper class.
    /// </summary>
    public sealed class MethodWrapperTypes
    {
        public Type[] Types { get; }

        public MethodWrapperTypes(IEnumerable<Type> types)
        {
            Types = new List<Type>(types).ToArray();
        }
    }
}
