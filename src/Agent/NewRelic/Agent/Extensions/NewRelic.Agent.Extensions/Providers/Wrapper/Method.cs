/*
* Copyright 2020 New Relic Corporation. All rights reserved.
* SPDX-License-Identifier: Apache-2.0
*/
using System;
using System.Reflection;

namespace NewRelic.Agent.Extensions.Providers.Wrapper
{
    public class Method
    {
        private static readonly ParameterInfo[] EMPTY_PARAMETER_INFO = new ParameterInfo[0];

        public readonly Type Type;
        public readonly string MethodName;
        public readonly string ParameterTypeNames;
        public readonly ParameterInfo[] Parameters;
        private readonly int _hashCode;

        public Method(Type type, string methodName, ParameterInfo[] parameters, string parameterTypeNames, int hashCode)
        {
            if (type == null)
                throw new ArgumentNullException("type");

            if (methodName == null)
                throw new ArgumentNullException("methodName");

            if (parameters == null)
                throw new ArgumentNullException("parameters");

            if (parameterTypeNames == null)
                throw new ArgumentNullException("parameterTypeNames");

            Type = type;
            Parameters = parameters;
            MethodName = methodName;
            ParameterTypeNames = parameterTypeNames;
            _hashCode = hashCode;
        }

        public Method(Type type, string methodName, string parameterTypeNames) :
            this(type, methodName, EMPTY_PARAMETER_INFO, parameterTypeNames, GetHashCode(type, methodName, parameterTypeNames))
        {
        }

        public override int GetHashCode()
        {
            return _hashCode;
        }

        private static int GetHashCode(Type type, string methodName, string parameterTypeNames)
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 23 + type.GetHashCode();
                hash = hash * 23 + methodName.GetHashCode();
                hash = hash * 23 + parameterTypeNames.GetHashCode();
                return hash;
            }
        }

        public override bool Equals(object other)
        {
            if (!(other is Method))
                return false;

            var otherMethod = (Method)other;

            if (otherMethod.Type != Type)
                return false;

            if (otherMethod.MethodName != MethodName)
                return false;

            if (otherMethod.ParameterTypeNames != ParameterTypeNames)
                return false;

            return true;
        }

        public override string ToString()
        {
            return string.Format("{0}:{1}({2})", Type.AssemblyQualifiedName, MethodName, ParameterTypeNames);
        }
    }
}
