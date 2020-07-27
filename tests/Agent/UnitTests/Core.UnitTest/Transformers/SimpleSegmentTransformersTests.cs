﻿using System;
using NewRelic.Agent.Configuration;
using NewRelic.Agent.Core.Aggregators;
using NewRelic.Agent.Core.Metrics;
using NewRelic.Agent.Core.NewRelic.Agent.Core.Timing;
using NewRelic.Agent.Core.Transformers.TransactionTransformer;
using NewRelic.Agent.Core.WireModels;
using NewRelic.Agent.Core.Wrapper.AgentWrapperApi.Builders;
using NewRelic.Agent.Core.Wrapper.AgentWrapperApi.Data;
using NewRelic.Collections;
using NUnit.Framework;
using Telerik.JustMock;

namespace NewRelic.Agent.Core.Transformers
{
    [TestFixture]
    public class SimpleSegmentTransformersTests
    {
        private IConfigurationService _configurationService;

        [SetUp]
        public void SetUp()
        {
            _configurationService = Mock.Create<IConfigurationService>();
        }

        #region Transform

        [Test]
        public void TransformSegment_NullStats()
        {
            const String name = "myname";
            var segment = GetSegment(name);

            //make sure it does not throw
            segment.AddMetricStats(null, _configurationService);
        }

        public void TransformSegment_AddParameter()
        {
            const String name = "myname";
            var segment = GetSegment(name);

            //make sure it does not throw
            segment.AddMetricStats(null, _configurationService);
        }

        [Test]
        public void TransformSegment_CreatesSegmentMetrics()
        {
            const String name = "name";
            var segment = GetSegment(name, 5);
            segment.ChildFinished(GetSegment("kid", 2));

            TransactionMetricName txName = new TransactionMetricName("WebTransaction", "Test", false);
            TransactionMetricStatsCollection txStats = new TransactionMetricStatsCollection(txName);
            segment.AddMetricStats(txStats, _configurationService);

            var scoped = txStats.GetScopedForTesting();
            var unscoped = txStats.GetUnscopedForTesting();

            Assert.AreEqual(1, scoped.Count);
            Assert.AreEqual(1, unscoped.Count);

            const String metricName = "DotNet/name";
            Assert.IsTrue(scoped.ContainsKey(metricName));
            Assert.IsTrue(unscoped.ContainsKey(metricName));

            var data = scoped[metricName];
            Assert.AreEqual(1, data.Value0);
            Assert.AreEqual(5, data.Value1);
            Assert.AreEqual(3, data.Value2);
            Assert.AreEqual(5, data.Value3);
            Assert.AreEqual(5, data.Value4);

            data = unscoped[metricName];
            Assert.AreEqual(1, data.Value0);
            Assert.AreEqual(5, data.Value1);
            Assert.AreEqual(3, data.Value2);
            Assert.AreEqual(5, data.Value3);
            Assert.AreEqual(5, data.Value4);
        }

        [Test]
        public void TransformSegment_TwoTransformCallsSame()
        {
            const String name = "name";
            var segment = GetSegment(name);

            var txName = new TransactionMetricName("WebTransaction", "Test", false);
            var txStats = new TransactionMetricStatsCollection(txName);
            segment.AddMetricStats(txStats, _configurationService);
            segment.AddMetricStats(txStats, _configurationService);

            var scoped = txStats.GetScopedForTesting();
            var unscoped = txStats.GetUnscopedForTesting();

            Assert.AreEqual(1, scoped.Count);
            Assert.AreEqual(1, unscoped.Count);

            const String metricName = "DotNet/name";
            Assert.IsTrue(scoped.ContainsKey(metricName));
            Assert.IsTrue(unscoped.ContainsKey(metricName));

            var nameScoped = scoped[metricName];
            var nameUnscoped = unscoped[metricName];

            Assert.AreEqual(2, nameScoped.Value0);
            Assert.AreEqual(2, nameUnscoped.Value0);
        }

        [Test]
        public void TransformSegment_TwoTransformCallsDifferent()
        {
            const String name = "name";
            var segment = GetSegment(name);

            const String name1 = "otherName";
            var segment1 = GetSegment(name1);

            var txName = new TransactionMetricName("WebTransaction", "Test", false);
            var txStats = new TransactionMetricStatsCollection(txName);
            segment.AddMetricStats(txStats, _configurationService);
            segment1.AddMetricStats(txStats, _configurationService);

            var scoped = txStats.GetScopedForTesting();
            var unscoped = txStats.GetUnscopedForTesting();

            Assert.AreEqual(2, scoped.Count);
            Assert.AreEqual(2, unscoped.Count);

            const String metricName = "DotNet/name";
            Assert.IsTrue(scoped.ContainsKey(metricName));
            Assert.IsTrue(unscoped.ContainsKey(metricName));

            var nameScoped = scoped[metricName];
            var nameUnscoped = unscoped[metricName];

            Assert.AreEqual(1, nameScoped.Value0);
            Assert.AreEqual(1, nameUnscoped.Value0);

            const String metricName1 = "DotNet/otherName";
            Assert.IsTrue(scoped.ContainsKey(metricName1));
            Assert.IsTrue(unscoped.ContainsKey(metricName1));

            nameScoped = scoped[metricName1];
            nameUnscoped = unscoped[metricName1];

            Assert.AreEqual(1, nameScoped.Value0);
            Assert.AreEqual(1, nameUnscoped.Value0);
        }

        #endregion Transform

        #region GetTransactionTraceName

        [Test]
        public void GetTransactionTraceName_ReturnsCorrectName()
        {
            const String name = "name";
            var segment = GetSegment(name);

            var transactionTraceName = segment.GetTransactionTraceName();

            Assert.AreEqual("name", transactionTraceName);
        }

        #endregion GetTransactionTraceName
        private static Segment GetSegment(String name)
        {
            var builder = new TypedSegment<SimpleSegmentData>(Mock.Create<ITransactionSegmentState>(), new MethodCallData("foo", "bar", 1), new SimpleSegmentData(name));
            builder.End();
            return builder;
        }

        public static TypedSegment<SimpleSegmentData> GetSegment(String name, double duration, TimeSpan start = new TimeSpan())
        {
            var methodCallData = new MethodCallData("foo", "bar", 1);
            return new TypedSegment<SimpleSegmentData>(start, TimeSpan.FromSeconds(duration), GetSegment(name));
        }
    }
}
