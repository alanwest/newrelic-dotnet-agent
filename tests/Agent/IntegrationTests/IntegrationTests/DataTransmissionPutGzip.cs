﻿using System.Collections.Generic;
using System.Linq;
using NewRelic.Agent.IntegrationTestHelpers;
using NewRelic.Agent.IntegrationTests.RemoteServiceFixtures;
using NewRelic.Agent.IntegrationTests.Shared.Models;
using Xunit;

namespace NewRelic.Agent.IntegrationTests
{
    public class DataTransmissionPutGzip : IClassFixture<MvcWithCollectorFixture>
    {
        private readonly MvcWithCollectorFixture _fixture;

        private IEnumerable<CollectedRequest> _collectedRequests = null;

        public DataTransmissionPutGzip(MvcWithCollectorFixture fixture)
        {
            _fixture = fixture;

            _fixture.AddActions(
                setupConfiguration: () =>
                {
                    var configModifier = new NewRelicConfigModifier(fixture.DestinationNewRelicConfigFilePath);

                    configModifier.PutForDataSend();
                    configModifier.CompressedContentEncoding("gzip");
                },
                exerciseApplication: () =>
                {
                    _fixture.Get();
                    _collectedRequests = _fixture.GetCollectedRequests();
                }
            );
            _fixture.Initialize();
        }

        [Fact]
        public void Test()
        {
            Assert.NotNull(_collectedRequests);
            var request = _collectedRequests.FirstOrDefault(x => x.Querystring.FirstOrDefault(y => y.Key == "method").Value == "connect");
            Assert.NotNull(request);
            Assert.True(request.Method == "PUT");
            Assert.True(request.ContentEncoding.First() == "gzip");
            var decompressedBody = Decompressor.GzipDecompress(request.RequestBody);
            Assert.NotEmpty(decompressedBody);
        }
    }
}
