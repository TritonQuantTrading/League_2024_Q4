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
using Accord.Math;
using Accord.Math.Optimization;
using Accord.Statistics;
using Accord;
using Fasterflect;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.Statistics;
#endregion
namespace QuantConnect.Algorithm.CSharp
{
    /*
     * public class QCAlgorithm
     * {
     *     SecurityManager Securities;               // Array of Security objects.
     *     SecurityPortfolioManager Portfolio;       // Array of SecurityHolding objects
     *     SecurityTransactionManager Transactions;  // Transactions helper
     *     ScheduleManager Schedule;                 // Scheduling helper
     *     NotificationManager Notify;               // Email, SMS helper
     *     UniverseManager Universe;                 // Universe helper
     *
     *     // Set up Requested Data, Cash, Time Period.
     *     public virtual void Initialize()
     *
     *     // Event Handlers: (Frequently Used)
     *     public virtual void OnData(Slice slice)
     *     public virtual void OnSecuritiesChanged(SecurityChanges changes)
     *     public virtual void OnEndOfDay(Symbol symbol)
     *     public virtual void OnEndOfAlgorithm()
     *     public virtual void OnWarmupFinished()
     *
     *     // Event Handlers: (Rarely Used)
     *     public virtual void OnSplits(Splits splits)
     *     public virtual void OnDividends(Dividends dividends)
     *     public virtual void OnDelistings(Delistings delistings)
     *     public virtual void OnSymbolChangedEvents(SymbolChangedEvents symbolsChanged)
     *     public virtual void OnMarginCall(List<SubmitOrderRequest> requests)
     *     public virtual void OnMarginCallWarning()
     *     public virtual void OnOrderEvent(OrderEvent orderEvent) // Async, requires locks for thread safety
     *     public virtual void OnAssignmentOrderEvent(OrderEvent assignmentEvent) // Async, requires locks for thread safety
     *     public virtual void OnBrokerageMessage(BrokerageMessageEvent messageEvent)
     *     public virtual void OnBrokerageDisconnect()
     *     public virtual void OnBrokerageReconnect()
     *
     *     // Indicator Helpers: (There are so many useful indicators)
     *     public AccelerationBands ABANDS(Symbol symbol, int period) { ... };
     *     ...
     *     public SimpleMovingAverage SMA(Symbol symbol, int period) { ... };
     *     ...
     *     public FilteredIdentity FilteredIdentity(Symbol symbol, TimeSpan resolution) { ... };
     * }
     */
    public class League2024Q4 : QCAlgorithm
    {
        // Public fields
        public const string DateFormat = "yyyy-MM-dd HH:mm:ss";
        public const decimal NearZero = 1e-6m;
        public const decimal NearZeroPct = 1e-4m;
        public const decimal InitialCash = 1_000_000;
        public const int PLookback = 252;
        public const int PNumCoarse = 200;
        public const int PNumFine = 70;
        public const int PNumLong = 5;
        public const decimal PAdjustmentStep = 1.0m;
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
        public bool _rebalance;
        private HashSet<Symbol> currentHoldings;
        private Dictionary<Symbol, decimal> targetWeights;
        private decimal adjustmentStep;
        private int _shortLookback;
        public DateTime? firstTradeDate;
        public DateTime? nextAdjustmentDate;

        // The QCAlgoritm only has a noargs constructor
        public League2024Q4()
        {
        }
        public override void Initialize()
        {
            // Set Dates (will be ignored in live mode)
            SetStartDate(2019, 3, 1);
            // SetStartDate(2019, 3, 1);
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
            // UniverseSettings.Asynchronous = true; // TODO: This would cause backtest consistency issues
            // UniverseSettings.ExtendedMarketHours = true; // only set to true if you are performing intraday trading
            // AddUniverseSelection(new FundamentalUniverseSelectionModel(Select, UniverseSettings));
            // AddUniverse(CoarseSelectionFunction);
            // AddUniverse(CoarseSelectionFunction, FineSelectionFunction);
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
            SetUniverseSelection(new MomentumUniverseSelectionModel(this, this._lookback, this._numCoarse, this._numFine, this._numLong, this.adjustmentStep, this._shortLookback));
            // SetPortfolioConstruction(new MinimumVariancePortfolioConstructionModel());
            // SetExecution(new ImmediateExecutionModel());
        }
        // public IEnumerable<Symbol> CoarseSelectionFunction(IEnumerable<CoarseFundamental> coarse)
        // {
        //     if (this.nextAdjustmentDate != null && Time < this.nextAdjustmentDate)
        //     {
        //         return Universe.Unchanged;
        //     }
        //     this._rebalance = true;
        //     if (this.firstTradeDate == null)
        //     {
        //         this.firstTradeDate = Time;
        //         this.nextAdjustmentDate = GetNextAdjustmentDate(Time);
        //         this._rebalance = true;
        //     }
        //     var selected = coarse.Where(x => x.HasFundamentalData && x.Price > 5)
        //         .OrderByDescending(x => x.DollarVolume).Take(this._numCoarse);
        //     return selected.Select(x => x.Symbol);
        // }
        // public IEnumerable<Symbol> FineSelectionFunction(IEnumerable<FineFundamental> fine)
        // {
        //     var selected = fine.OrderByDescending(f => f.MarketCap).Take(this._numFine);
        //     return selected.Select(x => x.Symbol);
        // }

        public override void OnWarmupFinished()
        {
            Log("Algorithm Ready");
            // show universities
            foreach (var universe in UniverseManager.Values)
            {
                Log($"Init Universe: {universe.Configuration.Symbol}: {universe.Members.Count} members");
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

            // Log the changes
            var currentDate = Time.ToString(DateFormat);
            foreach (var universe in UniverseManager.Values)
            {
                Log($"{currentDate}: Updated Universe: {universe.Configuration.Symbol}: {universe.Members.Count} members");
            }
            var addedStr = string.Join(", ", changes.AddedSecurities.Select(security => security.Symbol.Value));
            var removedStr = string.Join(", ", changes.RemovedSecurities.Select(security => security.Symbol.Value));
            Log($"{currentDate}: Security Changes: (+{changes.AddedSecurities.Count})[{addedStr}], (-{changes.RemovedSecurities.Count})[{removedStr}]");
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
            var sumOfAllHoldings = 0m;
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
            var targetedWeightsStr = string.Join(", ", this.targetWeights.OrderByDescending(kvp => kvp.Value).Select(kvp => $"{kvp.Key.Value}: {kvp.Value * 100:F2}%"));
            Log($"{currentDate}: Targeted Holdings: [{targetedWeightsStr}]");
            var holdingsStr = string.Join(", ", holdings.OrderByDescending(kvp => kvp.Value).Select(kvp => $"{kvp.Key}: {kvp.Value:F2}%"));
            Log($"{currentDate}: Holdings[{sumOfAllHoldings:F2}%]: [{holdingsStr}]");
        }

        public List<decimal> OptimizePortfolio(List<Symbol> selectedSymbols)
        {
            // historical returns matrix and symbols
            var (historicalReturns, validSymbols) = GetHistoricalReturnsMatrix(selectedSymbols);

            int nAssets = validSymbols.Count;
            if (nAssets == 0 || historicalReturns.GetLength(0) == 0)
            {
                Log("[OptimizePortfolio] No valid symbols with sufficient historical data. Returning empty weights.");
                return new List<decimal>();
            }

            // Portfolio Optimizers: [5 years] awesome (>= 300), good (>= 200), medium (>= 100), ordinary (< 100)
            var optimizer= new MonteCarloPortfolioOptimizer(PNPortfolios, this._shortLookback, PRandSeed); // awesome
            // var optimizer = new QuadraticProgrammingPortfolioOptimizer(this._shortLookback); // medium
            // var optimizer = new SOCPortfolioOptimizer(); // ordinary
            // var optimizer = new MaximumSharpeRatioPortfolioOptimizer(0, 1, 0.01); // medium
            // var optimizer = new MinimumVariancePortfolioOptimizer(); // good
            // var optimizer = new UnconstrainedMeanVariancePortfolioOptimizer(); // medium, but very unstable
            // var optimizer = new RiskParityPortfolioOptimizer(); // good
            var optimizedWeights = optimizer.Optimize(historicalReturns);

            // logging
            var symbolWeights = new Dictionary<Symbol, decimal>();
            for (int i = 0; i < nAssets; i++)
            {
                symbolWeights[validSymbols[i]] = (decimal)optimizedWeights[i];
            }
            var weightsStr = string.Join(", ", symbolWeights.OrderByDescending(kvp => kvp.Value).Select(kvp => $"{kvp.Key.Value}: {kvp.Value * 100:F2}%"));
            Log($"[OptimizePortfolio] Optimized Weights: {weightsStr}");

            return optimizedWeights.Select(w => (decimal)w).ToList();
        }

        private (double[,] historicalReturns, List<Symbol> validSymbols) GetHistoricalReturnsMatrix(List<Symbol> selectedSymbols)
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

            var validSymbols = new List<Symbol>();
            var returnsList = new List<List<double>>();

            foreach (var symbol in selectedSymbols)
            {
                if (!history.ContainsKey(symbol))
                {
                    Log($"[GetHistoricalReturnsMatrix] Missing historical data for {symbol.Value}. Skipping this symbol.");
                    continue;
                }

                var closePrices = history[symbol];
                if (closePrices.Count < shortLookback)
                {
                    Log($"[GetHistoricalReturnsMatrix] Insufficient historical data for {symbol.Value}. Required: {shortLookback}, Available: {closePrices.Count}. Skipping this symbol.");
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
                Log("[GetHistoricalReturnsMatrix] No valid symbols with sufficient historical data. Returning empty matrix.");
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