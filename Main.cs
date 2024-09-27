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
using System.Security.Cryptography.X509Certificates;
using QLNet;
using Accord;
using Fasterflect;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.Statistics;
#endregion
namespace QuantConnect.Algorithm.CSharp
{
    public class League2024Q4 : QCAlgorithm
    {
        // Public fields
        public const string DateFormat = "yyyy-MM-dd HH:mm:ss";
        public const decimal NearZero = 1e-6M;
        public const decimal NearZeroPct = 1e-4M;
        public const decimal InitialCash = 1_000_000;
        public const int PLookback = 252;
        public const int PNumCoarse = 200;
        public const int PNumFine = 70;
        public const int PNumLong = 5;
        public const decimal PAdjustmentStep = 1.0M;
        public const int PNPortfolios = 1000;
        public const int PShortLookback = 63;
        public const int PRandSeed = 11; // 18, 2, 4, 10, 11
        public const string PAdjustmentFrequency = "monthly"; // can be "daily", "weekly", "bi-weekly", "monthly"

        // Private fields
        private Dictionary<Symbol, MomentumPercent> _momp;
        private int _lookback;
        private int _numCoarse;
        private int _numFine;
        private int _numLong;
        private bool _rebalance;
        private HashSet<Symbol> currentHoldings;
        private Dictionary<Symbol, decimal> targetWeights;
        private decimal adjustmentStep;
        private int _shortLookback;
        private DateTime? firstTradeDate;
        private DateTime? nextAdjustmentDate;

        // The QCAlgoritm only has a noargs constructor
        public League2024Q4()
        {
        }
        public override void Initialize()
        {
            // Set Dates (will be ignored in live mode)
            SetStartDate(2014, 3, 1);
            // SetStartDate(2024, 1, 1);
            SetEndDate(2024, 8, 1);

            // Set Account Currency
            // - Default is USD $100,000
            // - Must be done before SetCash()
            // SetAccountCurrency("USD");
            // SetAccountCurrency("BTC", 10);
            //
            // Question: How to set multiple currencies? Like, the mix of USD and BTC
            // Answer: No, you cannot set multiple account currency and you can only set it once. Its internal valule is used for all calculations.
            // Reference: https://www.quantconnect.com/docs/v2/writing-algorithms/portfolio/cashbook#02-Account-Currency

            // Set Cash
            // SetCash("BTC", 10);
            SetCash(InitialCash);

            // Set Universe Settings
            UniverseSettings.Resolution = Resolution.Daily;
            // UniverseSettings.Asynchronous = true;
            // UniverseSettings.ExtendedMarketHours = true; // only set to true if you are performing intraday trading
            AddUniverse(CoarseSelectionFunction, FineSelectionFunction);
            // TODO: Multi-universe? But the members are only for certain unvierse, but the active securities are for all universes
            // UniverseManager[_universe.Configuration.Symbol].Members:
            // Universe.Members: When you remove an asset from a universe, LEAN usually removes the security from the Members collection and removes the security subscription. 
            // Question: question here, why `Members` exactly the same as `ActiveSecurities`?
            // Answer: ActiveSecurities is a collection of all Members from all universes.
            // TODO: what is the Symbol of a universe? Where is it defined?
            // ActiveSecurities: When you remove an asset from a universe, LEAN usually removes the security from the ActiveSecurities collection and removes the security subscription.
            // Note: both `UniverseManager` and `ActiveSecurities` are properties of the `QCAlgorithm` class
            // To have access to all securities without considering the active or not, use `Securities` property
            // - There are still cases where the Securities may remove the security, but only from the primary collection (Securities.Values), and can still be accessed from Securities.Total
            //
            // Universe.Selected: Different from Members, Members contains more assets 
            // Diffs Remarks:
            //   This set might be different than QuantConnect.Data.UniverseSelection.Universe.Securities
            //   which might hold members that are no longer selected but have not been removed
            //   yet, this can be because they have some open position, orders, haven't completed
            //   the minimum time in universe


            // Set Security Initializer
            // - This allow any custom security-level settings, instead of using the global universe settings
            // - SetSecurityInitializer(security => security.SetFeeModel(new ConstantFeeModel(0, "USD")));
            // - SetSecurityInitializer(new MySecurityInitializer(BrokerageModel, new FuncSecuritySeeder(GetLastKnownPrices)));
            SetSecurityInitializer(new BrokerageModelSecurityInitializer(
                this.BrokerageModel, new FuncSecuritySeeder(this.GetLastKnownPrices)
            ));

            // Set Warmup Period
            // SetWarmUp(PLookback/2, Resolution.Daily);

            // OnWarmupFinished() is the last method called before the algorithm starts running
            // - You can notify yourself by overriding this method: public override void OnWarmupFinished() { Log("Algorithm Ready"); }
            // - You can train machine learning models here: public override void OnWarmupFinished() { Train(MyTrainingMethod); }
            // The OnWarmupFinished() will be called after the warmup period even if the warmup period is not set

            // PostInitialize() method should never be overridden because it is used for predefined post-initialization routines

            // Customized Initialization
            this._momp = new Dictionary<Symbol, MomentumPercent>();
            this._lookback = PLookback;
            this._numCoarse = PNumCoarse;
            this._numFine = PNumFine;
            this._numLong = PNumLong;
            this._rebalance = false;
            this.currentHoldings = new HashSet<Symbol>();
            this.targetWeights = new Dictionary<Symbol, decimal>();
            this.adjustmentStep = PAdjustmentStep;
            this._shortLookback = PShortLookback;
            this.firstTradeDate = null;
            this.nextAdjustmentDate = null;
        }
        public IEnumerable<Symbol> CoarseSelectionFunction(IEnumerable<CoarseFundamental> coarse)
        {
            if (this.nextAdjustmentDate != null && Time < this.nextAdjustmentDate)
            {
                return Universe.Unchanged;
            }
            this._rebalance = true;
            if (this.firstTradeDate == null)
            {
                this.firstTradeDate = Time;
                this.nextAdjustmentDate = GetNextAdjustmentDate(Time);
                this._rebalance = true;
            }
            var selected = coarse.Where(x => x.HasFundamentalData && x.Price > 5)
                .OrderByDescending(x => x.DollarVolume).Take(this._numCoarse);
            return selected.Select(x => x.Symbol);
        }
        public IEnumerable<Symbol> FineSelectionFunction(IEnumerable<FineFundamental> fine)
        {
            var selected = fine.OrderByDescending(f => f.MarketCap).Take(this._numFine);
            return selected.Select(x => x.Symbol);
        }

        public override void OnWarmupFinished()
        {
            Log("Algorithm Ready");
            // show universities
            foreach (var universe in UniverseManager.Values)
            {
                Log($"Universe: {universe.Configuration.Symbol}");
                // show all members
                foreach (var member in universe.Members)
                {
                    Log($"  Member: {member}");
                }
            }
        }
        public static DateTime GetNextAdjustmentDate(DateTime currentDate)
        {
            if (PAdjustmentFrequency.Equals("weekly"))
            {
                return currentDate.AddDays(7);
            }
            else if (PAdjustmentFrequency.Equals("bi-weekly"))
            {
                return currentDate.AddDays(14);
            }
            else if (PAdjustmentFrequency.Equals("monthly"))
            {
                var nextMonth = currentDate.AddDays(32);
                return nextMonth.AddDays(-nextMonth.Day + 1);
            }
            else
            {
                throw new ArgumentException($"Unsupported adjustment frequency: {PAdjustmentFrequency}");
            }
        }
        public override void OnData(Slice slice)
        {
            foreach (var kvp in this._momp)
            {
                kvp.Value.Update(Time, Securities[kvp.Key].Close);
            }

            if (!this._rebalance)
            {
                return;
            }

            var sortedMom = (from kvp in this._momp
                             where kvp.Value.IsReady
                             orderby kvp.Value.Current.Value descending
                             select kvp.Key).ToList();
            var selected = sortedMom.Take(this._numLong).ToList();
            var newHoldings = new HashSet<Symbol>(selected);

            if (!newHoldings.SetEquals(this.currentHoldings) || this.firstTradeDate == Time)
            {
                if (selected.Count > 0)
                {
                    var optimalWeights = OptimizePortfolio(selected);
                    this.targetWeights = selected.Zip(optimalWeights, (k, v) => new { k, v }).ToDictionary(x => x.k, x => x.v);
                    this.currentHoldings = newHoldings;
                    AdjustPortfolio();
                }
            }

            this._rebalance = false;
            this.nextAdjustmentDate = GetNextAdjustmentDate(Time);
        }

        public override void OnSecuritiesChanged(SecurityChanges changes)
        {
            foreach (var symbol in changes.RemovedSecurities.Select(security => security.Symbol))
            {
                if (this._momp.Remove(symbol, out var _))
                {
                    Liquidate(symbol, "Removed from universe");
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
            var history = History(addedSymbols, 1 + this._lookback, Resolution.Daily); // iterable of slices
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

            // log the current universes and members
            var currentDate = Time.ToString(DateFormat);
            foreach (var universe in UniverseManager.Values)
            {
                Log($"{currentDate}: Universe: {universe.Configuration.Symbol}");
                foreach (var member in universe.Members)
                {
                    Log($"{currentDate}:   Member: {member.Key} => {member.Value}");
                }
            }
        }

        public void AdjustPortfolio()
        {
            var currentSymbols = Portfolio.Keys.ToHashSet();
            var targetSymbols = this.targetWeights.Keys.ToHashSet();

            var removedSymbols = currentSymbols.Except(targetSymbols);
            foreach (var symbol in removedSymbols)
            {
                Liquidate(symbol);
            }

            foreach (var kvp in this.targetWeights)
            {
                var symbol = kvp.Key;
                var targetWeight = kvp.Value;
                var currentWeight = Portfolio[symbol].Quantity / Portfolio.TotalPortfolioValue;
                if (!Portfolio.ContainsKey(symbol))
                {
                    currentWeight = 0;
                }
                var adjustedWeight = currentWeight * (1 - this.adjustmentStep) + targetWeight * this.adjustmentStep;
                SetHoldings(symbol, adjustedWeight);
            }

            var holdings = new Dictionary<string, decimal>();
            var sumOfAllHoldings = 0M;
            foreach (var symbol in Portfolio.Keys)
            {
                var holdingPercentage = Portfolio[symbol].HoldingsValue / Portfolio.TotalPortfolioValue * 100;
                if (holdingPercentage.IsGreaterThan(NearZeroPct))
                {
                    sumOfAllHoldings += holdingPercentage;
                    holdings[symbol.Value] = Math.Round(holdingPercentage, 2);
                }
            }
            var currentDate = Time.ToString(DateFormat);
            Log($"{currentDate}: Final holdings [{sumOfAllHoldings:F2}%]: {holdings}");
        }

        public List<decimal> OptimizePortfolio(List<Symbol> selectedSymbols)
        {
            var shortLookback = this._shortLookback;

            var historySlices = History(selectedSymbols, shortLookback, Resolution.Daily);

            var history = historySlices
                .SelectMany(slice => slice.Bars)
                .GroupBy(bar => bar.Key)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(g => (double)g.Value.Close).ToList()
                );

            var returnsDict = new Dictionary<Symbol, List<double>>();
            foreach (var symbol in selectedSymbols)
            {
                if (!history.ContainsKey(symbol))
                {
                    Log($"[OptimizePortfolio] Missing historical data for {symbol.Value}. Skipping this symbol.");
                    continue;
                }

                var closePrices = history[symbol];
                if (closePrices.Count < shortLookback)
                {
                    Log($"[OptimizePortfolio] Insufficient historical data for {symbol.Value}. Required: {shortLookback}, Available: {closePrices.Count}. Skipping this symbol.");
                    continue;
                }

                var returns = closePrices
                    .Select((price, index) => index == 0 ? 0.0 : (price - closePrices[index - 1]) / closePrices[index - 1])
                    .Skip(1) // Skip the first return as it's zero
                    .ToList();

                returnsDict[symbol] = returns;
            }

            var validSymbols = returnsDict.Keys.ToList();
            int nAssets = validSymbols.Count;

            if (nAssets == 0)
            {
                Log("[OptimizePortfolio] No valid symbols with sufficient historical data. Returning empty weights.");
                return new List<decimal>();
            }

            int nPortfolios = PNPortfolios;

            var portfolioReturns = new double[nPortfolios];
            var portfolioStdDevs = new double[nPortfolios];
            var sortinoRatios = new double[nPortfolios];
            var weightsRecord = new List<List<decimal>>(nPortfolios);

            var returnsMatrix = Matrix<double>.Build.Dense(nAssets, shortLookback - 1);
            for (int i = 0; i < nAssets; i++)
            {
                for (int j = 0; j < shortLookback - 1; j++)
                {
                    returnsMatrix[i, j] = returnsDict[validSymbols[i]][j];
                }
            }

            var covarianceMatrix = returnsMatrix * returnsMatrix.Transpose() / (shortLookback - 2);

            var random = new Random(PRandSeed);

            for (int i = 0; i < nPortfolios; i++)
            {
                var weights = new List<decimal>(nAssets);
                double sumWeights = 0.0;
                for (int j = 0; j < nAssets; j++)
                {
                    double w = random.NextDouble();
                    weights.Add((decimal)w);
                    sumWeights += w;
                }

                for (int j = 0; j < nAssets; j++)
                {
                    weights[j] = weights[j] / (decimal)sumWeights;
                }

                var weightsVector = Vector<double>.Build.Dense(nAssets, idx => (double)weights[idx]);

                double portfolioReturn = 0.0;
                for (int j = 0; j < nAssets; j++)
                {
                    portfolioReturn += returnsDict[validSymbols[j]].Average() * weightsVector[j];
                }
                portfolioReturn *= shortLookback;

                double portfolioVariance = weightsVector * covarianceMatrix * weightsVector;
                double portfolioStdDev = Math.Sqrt(portfolioVariance);

                double downsideSum = 0.0;
                for (int j = 0; j < nAssets; j++)
                {
                    var symbolReturns = returnsDict[validSymbols[j]];
                    foreach (var r in symbolReturns)
                    {
                        if (r < 0)
                        {
                            downsideSum += Math.Pow(r, 2) * (double)weights[j];
                        }
                    }
                }
                double downsideStdDev = Math.Sqrt(downsideSum / (shortLookback - 1));

                double sortinoRatio = downsideStdDev > 0 ? portfolioReturn / downsideStdDev : 0;

                portfolioReturns[i] = portfolioReturn;
                portfolioStdDevs[i] = portfolioStdDev;
                sortinoRatios[i] = sortinoRatio;
                weightsRecord.Add(new List<decimal>(weights));
            }

            int bestSortinoIndex = Array.IndexOf(sortinoRatios, sortinoRatios.Max());

            if (bestSortinoIndex < 0 || bestSortinoIndex >= weightsRecord.Count)
            {
                Log("[OptimizePortfolio] Unable to determine the best Sortino index. Returning equal weights.");
                var equalWeights = Enumerable.Repeat(1.0M / nAssets, nAssets).ToList();
                return equalWeights;
            }

            return weightsRecord[bestSortinoIndex];
        }

    }
}