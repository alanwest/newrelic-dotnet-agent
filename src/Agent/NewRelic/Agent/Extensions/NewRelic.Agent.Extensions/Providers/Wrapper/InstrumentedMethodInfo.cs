﻿using System;

namespace NewRelic.Agent.Extensions.Providers.Wrapper
{
    /// <summary>
    /// The immutable details about an instrumented method.
    /// </summary>
    public class InstrumentedMethodInfo
    {
        private readonly long _functionId;
        public readonly Method Method;
        public readonly String RequestedWrapperName;
        public readonly bool IsAsync;
        public readonly string RequestedMetricName;
        public readonly int? RequestedTransactionNamePriority;
        public readonly bool StartWebTransaction;

        public InstrumentedMethodInfo(long functionId, Method method, String requestedWrapperName, bool isAsync, string requestedMetricName, int? requestedTransactionNamePriority, bool startWebTransaction)
        {
            Method = method;
            RequestedWrapperName = requestedWrapperName;
            _functionId = functionId;
            IsAsync = isAsync;
            RequestedMetricName = requestedMetricName;
            RequestedTransactionNamePriority = requestedTransactionNamePriority;
            StartWebTransaction = startWebTransaction;
        }

        public override Int32 GetHashCode()
        {
            return _functionId.GetHashCode();
        }

        public override Boolean Equals(Object other)
        {
            if (!(other is InstrumentedMethodInfo))
                return false;

            var otherMethod = (InstrumentedMethodInfo)other;
            return _functionId == otherMethod._functionId;
        }
    }
}
