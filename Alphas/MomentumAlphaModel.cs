#region imports
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Drawing;
using QuantConnect;
using QuantConnect.Algorithm.Framework;
using QuantConnect.Algorithm.Framework.Selection;
using QuantConnect.Algorithm.Framework.Alphas;
using QuantConnect.Algorithm.Framework.Portfolio;
using QuantConnect.Algorithm.Framework.Portfolio.SignalExports;
using QuantConnect.Algorithm.Framework.Execution;
using QuantConnect.Algorithm.Framework.Risk;
using QuantConnect.Algorithm.Selection;
using QuantConnect.Api;
using QuantConnect.Parameters;
using QuantConnect.Benchmarks;
using QuantConnect.Brokerages;
using QuantConnect.Configuration;
using QuantConnect.Util;
using QuantConnect.Interfaces;
using QuantConnect.Algorithm;
using QuantConnect.Indicators;
using QuantConnect.Data;
using QuantConnect.Data.Auxiliary;
using QuantConnect.Data.Consolidators;
using QuantConnect.Data.Custom;
using QuantConnect.Data.Custom.IconicTypes;
using QuantConnect.DataSource;
using QuantConnect.Data.Fundamental;
using QuantConnect.Data.Market;
using QuantConnect.Data.Shortable;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Notifications;
using QuantConnect.Orders;
using QuantConnect.Orders.Fees;
using QuantConnect.Orders.Fills;
using QuantConnect.Orders.OptionExercise;
using QuantConnect.Orders.Slippage;
using QuantConnect.Orders.TimeInForces;
using QuantConnect.Python;
using QuantConnect.Scheduling;
using QuantConnect.Securities;
using QuantConnect.Securities.Equity;
using QuantConnect.Securities.Future;
using QuantConnect.Securities.Option;
using QuantConnect.Securities.Positions;
using QuantConnect.Securities.Forex;
using QuantConnect.Securities.Crypto;
using QuantConnect.Securities.CryptoFuture;
using QuantConnect.Securities.Interfaces;
using QuantConnect.Securities.Volatility;
using QuantConnect.Storage;
using QuantConnect.Statistics;
using QCAlgorithmFramework = QuantConnect.Algorithm.QCAlgorithm;
using QCAlgorithmFrameworkBridge = QuantConnect.Algorithm.QCAlgorithm;
#endregion

namespace QuantConnect
{

    public class MomentumAlphaModel : AlphaModel
    {
        // Public constants
        public const int PLookback = 252;
        public const int PLongLookback = 252;
        public const int PShortLookback = 252;
        public const int PNumLong = 5;
        public const int PNumShort = 0;
        public const Resolution PResolution = Resolution.Daily;

        // Parameters
        private readonly int _lookback;
        private readonly int _numLong;
        private readonly int _numShort;
        private readonly Resolution _resolution;
        private readonly TimeSpan _predictionInterval;

        // Symbol-specific data
        private readonly Dictionary<Symbol, SymbolData> _symbolDataDict;

        public MomentumAlphaModel(int lookback = PLongLookback, int numLong = PNumLong, int numShort = PNumShort, Resolution resolution = PResolution)
        {
            _lookback = lookback;
            _numLong = numLong;
            _numShort = numShort;
            _resolution = resolution;
            _predictionInterval = resolution.ToTimeSpan().Multiply(_lookback);

            _symbolDataDict = new Dictionary<Symbol, SymbolData>();
            Name = $"{nameof(MomentumAlphaModel)}({_lookback},{_numLong},{_numShort},{_resolution})";
        }

        public override IEnumerable<Insight> Update(QCAlgorithm algorithm, Slice data)
        {
            var insights = new List<Insight>();

            // Rank symbols based on ROC
            var rankedSymbols = _symbolDataDict
                .Where(kvp => kvp.Value.ROC.IsReady)
                .OrderByDescending(kvp => kvp.Value.ROC.Current.Value)
                .ToList();

            // Select top long and top short symbols
            var topLongs = rankedSymbols.Take(_numLong).Select(kvp => kvp.Key).ToList();
            var topShorts = rankedSymbols.TakeLast(_numShort).Select(kvp => kvp.Key).ToList();

            // Generate long insights
            foreach (var symbol in topLongs)
            {
                insights.Add(Insight.Price(symbol, _predictionInterval, InsightDirection.Up));
            }

            // Generate short insights
            foreach (var symbol in topShorts)
            {
                insights.Add(Insight.Price(symbol, _predictionInterval, InsightDirection.Down));
            }

            return insights;
        }

        public override void OnSecuritiesChanged(QCAlgorithm algorithm, SecurityChanges changes)
        {
            // Handle removed securities
            foreach (var removed in changes.RemovedSecurities)
            {
                if (_symbolDataDict.TryGetValue(removed.Symbol, out var symbolData))
                {
                    _symbolDataDict.Remove(removed.Symbol);
                    symbolData.Dispose();
                }
            }

            // Handle added securities
            foreach (var added in changes.AddedSecurities)
            {
                if (!_symbolDataDict.ContainsKey(added.Symbol) && added.Symbol.SecurityType == SecurityType.Equity)
                {
                    var symbolData = new SymbolData(algorithm, added.Symbol, _lookback, _resolution);
                    _symbolDataDict[added.Symbol] = symbolData;
                }
                else
                {
                    if (added.Symbol.SecurityType != SecurityType.Equity)
                    {
                        algorithm.Log($"[MomentumAlphaModel] {added.Symbol.Value} is not an equity security. Skipping.");
                    }
                }
            }
        }

        // Nested SymbolData class
        private class SymbolData : IDisposable
        {
            public Security Security { get; }
            public RateOfChange ROC { get; }
            private readonly IDataConsolidator _consolidator;
            private readonly SubscriptionManager _subscriptionManager;

            public SymbolData(QCAlgorithm algorithm, Symbol symbol, int lookback, Resolution resolution)
            {
                Security = algorithm.Securities[symbol];
                ROC = new RateOfChange(symbol.ToString(), lookback);
                _consolidator = algorithm.ResolveConsolidator(symbol, resolution);
                _subscriptionManager = algorithm.SubscriptionManager;

                algorithm.RegisterIndicator(symbol, ROC, _consolidator);
            }

            public void Dispose()
            {
                _subscriptionManager.RemoveConsolidator(Security.Symbol, _consolidator);
            }
        }

    }
}