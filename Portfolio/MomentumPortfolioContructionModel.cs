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
using Ionic.Zip;
using QuantConnect.Algorithm.Framework.Alphas.Analysis;
using Accord;
#endregion

namespace QuantConnect
{
    public class MomentumPortfolioConstructionModel : PortfolioConstructionModel
    {
        // public constants
        public const string DateFormat = "yyyy-MM-dd HH:mm:ss";
        public const decimal NearZero = 1e-6m;
        public const decimal NearZeroPct = 1e-4m;
        // readonly properties
        private readonly int _lookback;
        private readonly int _shortLookback;
        private readonly int _numLong;
        private readonly decimal _adjustmentStep;
        private readonly int _numPortfolios;
        private readonly int _randSeed;
        private readonly Dictionary<Symbol, MomentumPercent> _momp;
        private readonly IPortfolioOptimizer _optimizer;
        // properties
        private bool _rebalance;
        private HashSet<Symbol> _currentHoldings;
        private Dictionary<Symbol, decimal> _targetWeights;
        public MomentumPortfolioConstructionModel(int lookback, int shortLookback, int numLong, decimal adjustmentStep, int numPortfolios, int randSeed)
        {
            // Constructor arguments
            _lookback = lookback;
            _shortLookback = shortLookback;
            _numLong = numLong;
            _adjustmentStep = adjustmentStep;
            _randSeed = randSeed;

            // Other properties
            _momp = new Dictionary<Symbol, MomentumPercent>();
            _currentHoldings = new HashSet<Symbol>();
            _targetWeights = new Dictionary<Symbol, decimal>();

            // Choose a portfolio optimizer: [5 years] awesome (>= 300), good (>= 200), medium (>= 100), ordinary (< 100)
            _optimizer = new SortinoEfficientFrontierOptimizer(numPortfolios, shortLookback, randSeed); // awesome
            // _optimizer = new QuadraticProgrammingPortfolioOptimizer(shortLookback); // medium
            // _optimizer = new SOCPortfolioOptimizer(); // ordinary
            // _optimizer = new MaximumSharpeRatioPortfolioOptimizer(0, 1, 0.01); // medium
            // _optimizer = new MinimumVariancePortfolioOptimizer(); // good
            // _optimizer = new UnconstrainedMeanVariancePortfolioOptimizer(); // medium, but very unstable
            // _optimizer = new RiskParityPortfolioOptimizer(); // good
            
            // Mutable properties
            _rebalance = true;
        }
        // Create list of PortfolioTarget objects from Insights.
        public override List<PortfolioTarget> CreateTargets(QCAlgorithm algorithm, Insight[] insights)
        {
               foreach (var kvp in this._momp)
            {
                kvp.Value.Update(algorithm.Time, algorithm.Securities[kvp.Key].Close);
            }

            if (!this._rebalance)
            {
                // return;
            }

            var sortedMom = (from kvp in this._momp
                             where kvp.Value.IsReady
                             orderby kvp.Value.Current.Value descending
                             select kvp.Key).ToList();
            var selected = sortedMom.Take(this._numLong).ToList();
            var newHoldings = new HashSet<Symbol>(selected);

            if (!newHoldings.SetEquals(this._currentHoldings))
            {
                if (selected.Count > 0)
                {
                    var optimalWeights = OptimizePortfolio(algorithm, selected);
                    this._targetWeights = selected.Zip(optimalWeights, (k, v) => new { k, v }).ToDictionary(x => x.k, x => x.v);
                    this._currentHoldings = newHoldings;
                    AdjustPortfolio(algorithm);
                }
            }

            this._rebalance = false;
            var targets = new List<PortfolioTarget>();
            foreach (var kvp in this._targetWeights)
            {
                var symbol = kvp.Key;
                var weight = kvp.Value;
                var target = new PortfolioTarget(symbol, weight);
                targets.Add(target);
            }
            return targets;
        }

        // Determine if the portfolio should rebalance based on the provided rebalancing function.
        protected override bool IsRebalanceDue(Insight[] insights, DateTime algorithmUtc)
        {
            return base.IsRebalanceDue(insights, algorithmUtc);
        }

        // Determine the target percent for each insight.
        protected override Dictionary<Insight, double> DetermineTargetPercent(List<Insight> activeInsights)
        {
            return new Dictionary<Insight, double>();
        }

        // Get the target insights to calculate a portfolio target percent. They will be piped to DetermineTargetPercent().
        protected override List<Insight> GetTargetInsights()
        {
            return Algorithm.Insights.GetActiveInsights(Algorithm.UtcTime).ToList();
        }

        // Determine if the portfolio construction model should create a target for this insight.
        protected override bool ShouldCreateTargetForInsight(Insight insight)
        {
            return base.ShouldCreateTargetForInsight(insight);
        }

        // Security change details.
        public override void OnSecuritiesChanged(QCAlgorithm algorithm, SecurityChanges changes)
        {
            base.OnSecuritiesChanged(algorithm, changes);

            foreach (var symbol in changes.RemovedSecurities.Select(security => security.Symbol))
            {
                if (this._momp.Remove(symbol, out var _))
                {
                    algorithm.Liquidate(symbol, "Removed from universe");
                }
            }

            foreach (var symbol in changes.AddedSecurities.Select(security => security.Symbol))
            {
                if (!this._momp.ContainsKey(symbol))
                {
                    this._momp[symbol] = new MomentumPercent(this._lookback);
                }
            }
            var addedSymbols = (from kvp in this._momp
                                where !kvp.Value.IsReady
                                select kvp.Key).ToList();
            var history = algorithm.History(addedSymbols, 1 + this._lookback, Resolution.Daily); // iterable of slices
            foreach (var symbol in addedSymbols)
            {
                var symbolHistory = history.Where(slice => slice.Bars.ContainsKey(symbol));
                foreach (var slice in symbolHistory)
                {
                    var time = slice.Time;
                    var value = slice.Bars[symbol].Close;
                    var item = new IndicatorDataPoint(symbol, time, value);
                    this._momp[symbol].Update(item);
                }
            }

        }
        // Customized helper methods
        public void AdjustPortfolio(QCAlgorithm algorithm)
        {
            var currentSymbols = algorithm.Portfolio.Keys.ToHashSet();
            var targetSymbols = this._targetWeights.Keys.ToHashSet();

            var removedSymbols = currentSymbols.Except(targetSymbols);
            foreach (var symbol in removedSymbols)
            {
                algorithm.Liquidate(symbol);
            }

            foreach (var kvp in this._targetWeights)
            {
                var symbol = kvp.Key;
                var targetWeight = kvp.Value;
                var currentWeight = algorithm.Portfolio[symbol].Quantity / algorithm.Portfolio.TotalPortfolioValue;
                if (!algorithm.Portfolio.ContainsKey(symbol))
                {
                    currentWeight = 0;
                }
                var adjustedWeight = currentWeight * (1 - this._adjustmentStep) + targetWeight * this._adjustmentStep;
                algorithm.SetHoldings(symbol, adjustedWeight);
            }

            var holdings = new Dictionary<string, decimal>();
            var sumOfAllHoldings = 0m;
            foreach (var symbol in algorithm.Portfolio.Keys)
            {
                var holdingPercentage = algorithm.Portfolio[symbol].HoldingsValue / algorithm.Portfolio.TotalPortfolioValue * 100;
                if (holdingPercentage.IsGreaterThan(NearZeroPct))
                {
                    sumOfAllHoldings += holdingPercentage;
                    holdings[symbol.Value] = Math.Round(holdingPercentage, 2);
                }
            }
            var currentDate = algorithm.Time.ToString(DateFormat);
            var targetedWeightsStr = string.Join(", ", this._targetWeights.OrderByDescending(kvp => kvp.Value).Select(kvp => $"{kvp.Key.Value}: {kvp.Value * 100:F2}%"));
            algorithm.Log($"{currentDate}: Targeted Holdings: [{targetedWeightsStr}]");
            var holdingsStr = string.Join(", ", holdings.OrderByDescending(kvp => kvp.Value).Select(kvp => $"{kvp.Key}: {kvp.Value:F2}%"));
            algorithm.Log($"{currentDate}: Holdings[{sumOfAllHoldings:F2}%]: [{holdingsStr}]");
        }public List<decimal> OptimizePortfolio(QCAlgorithm algorithm, List<Symbol> selectedSymbols)
        {
            // historical returns matrix and symbols
            var (historicalReturns, validSymbols) = GetHistoricalReturnsMatrix(algorithm, selectedSymbols);

            int nAssets = validSymbols.Count;
            if (nAssets == 0 || historicalReturns.GetLength(0) == 0)
            {
                algorithm.Log("[OptimizePortfolio] No valid symbols with sufficient historical data. Returning empty weights.");
                return new List<decimal>();
            }

            // Portfolio Optimizers: [5 years] awesome (>= 300), good (>= 200), medium (>= 100), ordinary (< 100)
            var optimizer = this._optimizer;
            var optimizedWeights = optimizer.Optimize(historicalReturns);

            // logging
            var symbolWeights = new Dictionary<Symbol, decimal>();
            for (int i = 0; i < nAssets; i++)
            {
                symbolWeights[validSymbols[i]] = (decimal)optimizedWeights[i];
            }
            var weightsStr = string.Join(", ", symbolWeights.OrderByDescending(kvp => kvp.Value).Select(kvp => $"{kvp.Key.Value}: {kvp.Value * 100:F2}%"));
            algorithm.Log($"[OptimizePortfolio] Optimized Weights: {weightsStr}");
            return optimizedWeights.Select(w => (decimal)w).ToList();
        }

        private (double[,] historicalReturns, List<Symbol> validSymbols) GetHistoricalReturnsMatrix(QCAlgorithm algorithm, List<Symbol> selectedSymbols)
        {
            var shortLookback = this._shortLookback;

            // TODO: methods to get historical returns matrix,
            var historySlices = algorithm.History(selectedSymbols, shortLookback, Resolution.Daily);

            var history = historySlices
                .SelectMany(slice => slice.Bars)
                .GroupBy(bar => bar.Key)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(g => (double)g.Value.Close).ToList()
                );

            var validSymbols = new List<Symbol>();
            var returnsList = new List<List<double>>();

            foreach (var symbol in selectedSymbols)
            {
                if (!history.ContainsKey(symbol))
                {
                    algorithm.Log($"[GetHistoricalReturnsMatrix] Missing historical data for {symbol.Value}. Skipping this symbol.");
                    continue;
                }

                var closePrices = history[symbol];
                if (closePrices.Count < shortLookback)
                {
                    algorithm.Log($"[GetHistoricalReturnsMatrix] Insufficient historical data for {symbol.Value}. Required: {shortLookback}, Available: {closePrices.Count}. Skipping this symbol.");
                    continue;
                }

                var returns = closePrices
                    .Select((price, index) => index == 0 ? 0.0 : (price - closePrices[index - 1]) / closePrices[index - 1])
                    .Skip(1) // Skip the first return as it's zero
                    .ToList();

                validSymbols.Add(symbol);
                returnsList.Add(returns);
            }

            int nAssets = validSymbols.Count;

            if (nAssets == 0)
            {
                algorithm.Log("[GetHistoricalReturnsMatrix] No valid symbols with sufficient historical data. Returning empty matrix.");
                return (new double[0, 0], validSymbols);
            }

            int nObservations = returnsList[0].Count; // Assuming all assets have the same number of observations
            var historicalReturns = new double[nObservations, nAssets];

            for (int i = 0; i < nAssets; i++)
            {
                var returns = returnsList[i];
                for (int j = 0; j < nObservations; j++)
                {
                    historicalReturns[j, i] = returns[j];
                }
            }

            return (historicalReturns, validSymbols);
        }
    }
}