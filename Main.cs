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
     *     // Doc: https://www.quantconnect.com/docs/v2/writing-algorithms/key-concepts/event-handlers
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
     *     // Recommend: not to place orders in the OnOrderEvent to avoid infinite loops
     *     public virtual void OnOrderEvent(OrderEvent orderEvent) // Async, requires locks for thread safety
     *     public virtual void OnAssignmentOrderEvent(OrderEvent assignmentEvent) // Async, requires locks for thread safety, for options assignment events
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
        private int _numCoarse;
        private int _numFine;
        // The QCAlgoritm only has a noargs constructor
        public League2024Q4()
        {
        }
        public override void Initialize()
        {
            /************************************************************/
            /**            Start Customized Initialization             **/
            this._numCoarse = PNumCoarse;
            this._numFine = PNumFine;
            /**             End Customized Initialization              **/
            /************************************************************/

            /************************************************************/
            /**             Start Default Initialization               **/
            // Set Dates (will be ignored in live mode)
            SetStartDate(2019, 3, 1);
            SetEndDate(2024, 8, 1);

            // Set Account Currency
            // - Default is USD $100,000
            // - Must be done before SetCash()
            // SetAccountCurrency("USD");
            // SetAccountCurrency("BTC", 10);
            //
            // Question: How to set multiple currencies? For example, if you want to use a mix of USD and BTC
            // Answer: No, you cannot set multiple account currency and you can only set it once. Its internal valule is used for all calculations.
            // Reference: https://www.quantconnect.com/docs/v2/writing-algorithms/portfolio/cashbook#02-Account-Currency

            // Set Cash
            // SetCash("BTC", 10);
            SetCash(InitialCash);
            /**               End Default Initialization               **/
            /************************************************************/

            /************************************************************/
            /**               Start Algorithm Framework               **/
            // Set Universe Settings
            UniverseSettings.Resolution = Resolution.Daily;
            // UniverseSettings.Asynchronous = true; // This would cause backtest consistency issues, see: https://www.quantconnect.com/docs/v2/writing-algorithms/algorithm-framework/universe-selection/universe-settings#09-Asynchronous-Selection
            // UniverseSettings.ExtendedMarketHours = true; // only set to true if you are performing intraday trading
            // AddUniverseSelection(new FundamentalUniverseSelectionModel(Select, UniverseSettings));

            // Docs:
            // Universe.Members: When you remove an asset from a universe, LEAN usually removes the security from the Members collection and removes the security subscription.
            // ActiveSecurities: When you remove an asset from a universe, LEAN usually removes the security from the ActiveSecurities collection and removes the security subscription.
            //
            // Question: What's the differences between `Universe.Members` and `Universe.ActiveSecurities`?
            // Answer: `ActiveSecurities` is a collection of all `Members` from all universes.

            // UniverseManager[_universe.Configuration.Symbol].Members:
            // FIXME: Multiple universes are allowed? But the members are only for certain unvierse, but the active securities are for all universes
            // FIXME: what is the Symbol of a universe? Where is it defined?
            // Note: both `UniverseManager` and `ActiveSecurities` are properties of the `QCAlgorithm` class
            // To have access to all securities without considering they are activthee or not, use `Securities` property
            // - There are still cases where the Securities may remove the security, but only from the primary collection (Securities.Values), and can still be accessed from Securities.Total
            //
            // Universe.Selected: Different from Members, Members may contain more assets
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
            // Set Selection
            SetUniverseSelection(new BasicUniverseSelectionModel(PNumCoarse, PNumFine));
            // Set Alphas
            SetAlpha(new TempAlphaModel());
            // Set Portfolio
            SetPortfolioConstruction(new MomentumPortfolioConstructionModel(PLookback, PShortLookback, PNumLong, PAdjustmentStep, PNPortfolios, PRandSeed));
            // Set Risk 
            // Set Execution
            SetExecution(new ImmediateExecutionModel());
            /**                End Algorithm Framework                 **/
            /************************************************************/

            // Set Warmup Period
            // SetWarmUp(PLookback/2, Resolution.Daily);

            // OnWarmupFinished() is the last method called before the algorithm starts running
            // - You can notify yourself by overriding this method: public override void OnWarmupFinished() { Log("Algorithm Ready"); }
            // - You can train machine learning models here: public override void OnWarmupFinished() { Train(MyTrainingMethod); }
            // The OnWarmupFinished() will be called after the warmup period even if the warmup period is not set

            // PostInitialize() method should never be overridden because it is used for predefined post-initialization routines
        }
        public override void OnWarmupFinished()
        {
            Log("Algorithm Ready");
            // show universities
            foreach (var universe in UniverseManager.Values)
            {
                Log($"Init Universe: {universe.Configuration.Symbol}: {universe.Members.Count} members");
            }
        }
        
        public override void OnData(Slice slice)
        {
            // Log($"OnData: {Time}, {slice.Keys.Count} symbols, {slice.Bars.Count} bars");
            // The suggested way of handling the time-based event is using the Scheduled Events 
            // instead of checking the time in the OnData method
            // Source: https://www.quantconnect.com/docs/v2/writing-algorithms/scheduled-events
            /*
              Scheduled Events let you trigger code to run at specific times of day, regardless of
              your algorithm's data subscriptions. It's easier and more reliable to execute
              time-based events with Scheduled Events than checking the current algorithm time in
              the OnData event handler.
            */
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

    }
}