﻿using System;
using System.Collections.Generic;
using System.Linq;
using NewRelic.Agent.IntegrationTestHelpers;
using NewRelic.Agent.IntegrationTestHelpers.Models;
using NewRelic.Agent.IntegrationTests.RemoteServiceFixtures;
using NewRelic.Testing.Assertions;
using Xunit;
using Xunit.Abstractions;

namespace NewRelic.Agent.IntegrationTests
{
    public class CustomAttributesLegacyIgnored : IClassFixture<CustomAttributesWebApi>
    {
        private readonly CustomAttributesWebApi _fixture;

        public CustomAttributesLegacyIgnored(CustomAttributesWebApi fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _fixture.TestLogger = output;
            _fixture.Actions(
                setupConfiguration: () =>
                {
                    var configPath = fixture.DestinationNewRelicConfigFilePath;
                    var configModifier = new NewRelicConfigModifier(configPath);
                    configModifier.ForceTransactionTraces();

                    CommonUtils.ModifyOrCreateXmlNodeInNewRelicConfig(configPath,
                        new[] { "configuration", "parameterGroups", "customParameters" }, "ignore", "key");
                },
                exerciseApplication: () =>
                {
                    _fixture.Get();
                    _fixture.AgentLog.WaitForLogLine(AgentLogFile.TransactionSampleLogLineRegex, TimeSpan.FromMinutes(2));
                }

                );
            _fixture.Initialize();
        }

        [Fact]
        public void Test()
        {
            var expectedTransactionTraceAttributes = new Dictionary<String, String>
            {
                { "foo", "bar" }
            };
            var unexpectedTransactionTraceAttributes = new List<String>
            {
                "key"
            };

            var transactionSample = _fixture.AgentLog.GetTransactionSamples().FirstOrDefault();
            Assert.NotNull(transactionSample);
            var maybeDeprecationMessage = _fixture.AgentLog.TryGetLogLine(@".*NewRelic WARN: Deprecated configuration property 'parameterGroups.customParameters.ignore'.  Use 'attributes.exclude'.  See http://docs.newrelic.com for details.");

            NrAssert.Multiple
            (
                () => Assertions.TransactionTraceHasAttributes(expectedTransactionTraceAttributes, TransactionTraceAttributeType.User, transactionSample),
                () => Assertions.TransactionTraceDoesNotHaveAttributes(unexpectedTransactionTraceAttributes, TransactionTraceAttributeType.User, transactionSample),
                () => Assert.NotNull(maybeDeprecationMessage)
            );
        }
    }
}
