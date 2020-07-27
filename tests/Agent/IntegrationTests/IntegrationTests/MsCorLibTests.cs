﻿using System;
using System.Linq;
using System.Net;
using NewRelic.Agent.IntegrationTestHelpers;
using Xunit;
using Xunit.Abstractions;

namespace NewRelic.Agent.IntegrationTests
{
    public class MsCorLibTests : IClassFixture<RemoteServiceFixtures.BasicMvcApplication>
    {
        private readonly RemoteServiceFixtures.BasicMvcApplication _fixture;

        public MsCorLibTests(RemoteServiceFixtures.BasicMvcApplication fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _fixture.TestLogger = output;
            _fixture.Actions
            (
                setupConfiguration: () =>
                {
                    var instrumentationFilePath = $@"{fixture.DestinationNewRelicExtensionsDirectoryPath}\CustomInstrumentation.xml";

                    //System.IO.Path.Combine seems to always be hit by MVC. Could move to console application w/ direct execution if ever have issues.
                    CommonUtils.AddCustomInstrumentation(instrumentationFilePath, "mscorlib", "System.IO.Path", "Combine");
                },
                exerciseApplication: () =>
                {
                    try
                    {
                        _fixture.Get();
                    }
                    catch (WebException ex)
                    {
                        throw new Exception("Application with mscorlib custom instrumentation crashed.", ex);

                        // Due to reliance on the managed log existing for integration test execution, we can't gracefully catch 
                        // then assert later in the actual test case. Would be nice to fix all of this long-term... 
                        // i.e. optionally wait for logs / have other mechanisms.
                    }
                }
            );
            _fixture.Initialize();
        }

        [Fact]
        public void ShouldNotCrashWhenAttemptToInstrumentMsCorLib()
        {
            // NOTE: Crashing case under test will not get to this method. Throws exception inside of exerciseApplication.
            var metricCount = _fixture.AgentLog.GetMetrics().Count();
            Assert.True(metricCount > 0);
        }
    }
}
