/*
* Copyright 2020 New Relic Corporation. All rights reserved.
* SPDX-License-Identifier: Apache-2.0
*/
using System.Reflection;
using NewRelic.Agent.Core.Tracer;

namespace NewRelic.Agent.Core
{
    public sealed class DisabledAgentManager : IAgentManager
    {
        public ITracer GetTracerImpl(string tracerFactoryName, uint tracerArguments, string metricName, string assemblyName, MethodBase method, string typename, string methodName, string argumentSignature, object invocationTarget, object[] arguments, ulong functionId)
        {
            return null;
        }
    }
}
