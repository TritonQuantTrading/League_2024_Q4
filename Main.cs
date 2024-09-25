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
#endregion
namespace QuantConnect.Algorithm.CSharp
{
    public class MonthlyRebalancingWithEarlyStopCSharp : QCAlgorithm
    {
        // Public fields
        public const string DateFormat = "yyyy-MM-dd HH:mm:ss";
        public const decimal NearZero = 1e-6M;
        public const decimal NearZeroPct = 1e-4M;
        public const int PLookback = 252;
        public const int PNumCoarse = 200;
        public const int PNumFine = 70;
        public const int PNumLong = 5;
        public const decimal PAdjustmentStep = 1.0M;
        public const int PNPortfolios = 1000;
        public const int PShortLookback = 63;
        public const int PRandSeed = 13;
        public const string PAdjustmentFrequency = "monthly"; // can be "daily", "weekly", "bi-weekly", "monthly"
        // Private fields
        private Dictionary<Symbol, MomentumPercent> _momp;
        private int _lookback;
        private int _num_coarse;
        private int _num_fine;
        private int _num_long;
        private bool _rebalance;
        private HashSet<Symbol> current_holdings;
        private Dictionary<Symbol, decimal> target_weights;
        private decimal adjustment_step;
        private int _short_lookback;

        private DateTime first_trade_date;
        private DateTime next_adjustment_date;

        // Metrics for no trades and profit tracking
        private int no_trade_days;
        private decimal highest_profit;
        private decimal lowest_profit;
        private decimal monthly_starting_equity;
        private int last_logged_month;
        private bool global_stop_loss_triggered;
        private bool halved_lookback;
        // the QCAlgoritm only has a noargs constructor
        public MonthlyRebalancingWithEarlyStopCSharp()
        {

        }
        public override void Initialize()
        {
            SetStartDate(2019, 3, 1);
            SetEndDate(2024, 8, 1);
            SetCash(1_000_000);
            SetSecurityInitializer(new BrokerageModelSecurityInitializer(
                this.BrokerageModel, new FuncSecuritySeeder(this.GetLastKnownPrices)
            ));
            UniverseSettings.Resolution = Resolution.Daily;
            // should be init here instead of constructor
            this._momp = new Dictionary<Symbol, MomentumPercent>();

            this._lookback = PLookback;
            this._num_coarse = PNumCoarse;
            this._num_fine = PNumFine;
            this._num_long = PNumLong;
            this._rebalance = false;
            this.current_holdings = new HashSet<Symbol>();
            this.target_weights = new Dictionary<Symbol, decimal>();
            this.adjustment_step = PAdjustmentStep;
            this._short_lookback = PShortLookback;
            this.first_trade_date = DateTime.MinValue;
            this.next_adjustment_date = DateTime.MinValue;
            this.no_trade_days = 0;
            this.highest_profit = 0;
            this.lowest_profit = decimal.MaxValue;
            this.monthly_starting_equity = 0;
            this.last_logged_month = 0;
            this.global_stop_loss_triggered = false;
            this.halved_lookback = false;
            AddUniverse(CoarseSelectionFunction, FineSelectionFunction);
        }
        public IEnumerable<Symbol> CoarseSelectionFunction(IEnumerable<CoarseFundamental> coarse)
        {
            if (this.next_adjustment_date != DateTime.MinValue && Time < this.next_adjustment_date)
            {
                return Universe.Unchanged;
            }
            this._rebalance = true;
            if (this.first_trade_date == DateTime.MinValue)
            {
                this.first_trade_date = Time;
                this.next_adjustment_date = GetNextAdjustmentDate(Time);
                this._rebalance = true;
            }
            var selected = coarse.Where(x => x.HasFundamentalData && x.Price > 5)
                .OrderByDescending(x => x.DollarVolume)
                .Take(this._num_coarse);
            return selected.Select(x => x.Symbol);
        }

        public IEnumerable<Symbol> FineSelectionFunction(IEnumerable<FineFundamental> fine)
        {
            var selected = fine.OrderByDescending(f => f.MarketCap).Take(this._num_fine);
            return selected.Select(x => x.Symbol);
        }

        public static DateTime GetNextAdjustmentDate(DateTime currentDate, bool initial = false)
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
                if (initial)
                {
                    var nextMonth = currentDate.AddDays(32);
                    return nextMonth.AddDays(-nextMonth.Day + 1);
                }
                else
                {
                    var nextMonth = currentDate.AddDays(32);
                    return nextMonth.AddDays(-nextMonth.Day + 1);
                }
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

            if (this.monthly_starting_equity.IsLessThanOrEqual(NearZero))
            {
                this.monthly_starting_equity = this.Portfolio.TotalPortfolioValue;
            }

            var current_portfolio_value = this.Portfolio.TotalPortfolioValue;

            var current_profit_pct_to_start = 0M;
            if (this.monthly_starting_equity.IsGreaterThan(NearZero))
            {
                current_profit_pct_to_start = ((current_portfolio_value - this.monthly_starting_equity) / this.monthly_starting_equity) * 100;
            }

            this.highest_profit = Math.Max(this.highest_profit, current_profit_pct_to_start);

            var drop_pct = 0M;
            if (this.highest_profit.IsGreaterThan(NearZero))
            {
                drop_pct = ((this.highest_profit - current_profit_pct_to_start) / this.highest_profit) * 100;
            }

            if (current_profit_pct_to_start <= -12 && !this.global_stop_loss_triggered)
            {
                var current_date = Time.ToString(DateFormat);
                Log($"{current_date}: Liquidating all holdings due to a portfolio loss of {current_profit_pct_to_start:.2f}% (stop-loss from last adjustment).");
                Liquidate();
                this._rebalance = false;  // Allow immediate rebalancing
                this.global_stop_loss_triggered = true;
                this.highest_profit = 0;
                this.monthly_starting_equity = 0;
                this.next_adjustment_date = GetNextAdjustmentDate(Time);
                Log($"{current_date}: Stopping trading temporarily due to stop-loss trigger.");
                return;
            }


            if (this.highest_profit > 10 && drop_pct >= 10)
            {
                var current_date = Time.ToString(DateFormat);
                Log($"{current_date}: Liquidating all holdings due to a {drop_pct:.2f}% drop in profit (take-profit).");
                Log($"{current_date}: Highest Net Profit: {this.highest_profit:.2f}% (from last adjustment)");
                Log($"{current_date}: Current Net Profit: {current_profit_pct_to_start:.2f}% (from last adjustment)");
                var total_profit_pct = ((current_portfolio_value - Portfolio.CashBook["USD"].Amount) / Portfolio.CashBook["USD"].Amount) * 100;
                Log($"{current_date}: Total Net Profit: {total_profit_pct:.2f}% (from inception)");
                Liquidate();
                this._rebalance = true;  // Allow immediate rebalancing
                this.global_stop_loss_triggered = true;
                this.highest_profit = 0;
                this.monthly_starting_equity = 0;
                this.next_adjustment_date = GetNextAdjustmentDate(Time);
                return;
            }


            if (Time.Day == 1 && (Time.Month != this.last_logged_month))
            {
                var current_date = Time.ToString(DateFormat);
                var portfolio_value = Portfolio.TotalPortfolioValue;
                var net_profit = portfolio_value - Portfolio.CashBook["USD"].Amount;
                var holdings_value = Portfolio.Values.Where(sec => sec.Invested).Sum(sec => sec.HoldingsValue);
                var unrealized_profit = Portfolio.TotalUnrealizedProfit;
                var return_pct = (net_profit / Portfolio.CashBook["USD"].Amount) * 100;
                Log($"{current_date}: Equity: ${portfolio_value:.2f} | Holdings: ${holdings_value:.2f} | Net Profit: ${net_profit:.2f} | Unrealized: ${unrealized_profit:.2f} | Return: {return_pct:.2f}%");
                this.last_logged_month = Time.Month;

                if (this.halved_lookback)
                {
                    this._lookback = PLookback;  // Restore the original lookback period
                    this._short_lookback = PShortLookback;  // Restore the original short lookback period
                    this.halved_lookback = false;  // Reset the halved lookback flag
                }
            }

            if (!this._rebalance)
            {
                return;
            }

            if (this._rebalance)
            {
                this.global_stop_loss_triggered = false;
                this._rebalance = false;
            }

            var sorted_mom = (from kvp in this._momp
                              where kvp.Value.IsReady
                              orderby kvp.Value.Current.Value descending
                              select kvp.Key).ToList();
            var selected = sorted_mom.Take(this._num_long).ToList();
            var new_holdings = new HashSet<Symbol>(selected);

            if (!new_holdings.SetEquals(this.current_holdings) || this.first_trade_date == Time)
            {
                if (selected.Count > 0)
                {
                    var optimal_weights = OptimizePortfolio(selected);
                    this.target_weights = selected.Zip(optimal_weights, (k, v) => new { k, v }).ToDictionary(x => x.k, x => x.v);
                    this.current_holdings = new_holdings;
                    AdjustPortfolio();
                }
            }

            this._rebalance = false;
            this.next_adjustment_date = GetNextAdjustmentDate(Time);
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

            var added_symbols = (from kvp in this._momp
                                 where !kvp.Value.IsReady
                                 select kvp.Key).ToList();
            var history = History(added_symbols, 1 + this._lookback, Resolution.Daily); // iterable of slices
            foreach (var symbol in added_symbols)
            {
                var ticker = symbol.Value;
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

        public void AdjustPortfolio()
        {
            var current_symbols = Portfolio.Keys.ToHashSet();
            var target_symbols = this.target_weights.Keys.ToHashSet();

            // Liquidate removed symbols
            var removed_symbols = current_symbols.Except(target_symbols);
            foreach (var symbol in removed_symbols)
            {
                Liquidate(symbol);
            }

            // Adjust holdings for selected symbols
            foreach (var kvp in this.target_weights)
            {
                var symbol = kvp.Key;
                var target_weight = kvp.Value;
                var current_weight = Portfolio[symbol].Quantity / Portfolio.TotalPortfolioValue;
                if (!Portfolio.ContainsKey(symbol))
                {
                    current_weight = 0;
                }
                var adjusted_weight = current_weight * (1 - this.adjustment_step) + target_weight * this.adjustment_step;
                SetHoldings(symbol, adjusted_weight);
            }

            var holdings = new Dictionary<string, decimal>();
            var sum_of_all_holdings = 0M;
            foreach (var symbol in Portfolio.Keys)
            {
                var holding_percentage = Portfolio[symbol].HoldingsValue / Portfolio.TotalPortfolioValue * 100;
                if (holding_percentage.IsGreaterThan(NearZeroPct))
                {
                    sum_of_all_holdings += holding_percentage;
                    holdings[symbol.Value] = Math.Round(holding_percentage, 2);
                }
            }
            var current_date = Time.ToString(DateFormat);
            Log($"{current_date}: Final holdings [{sum_of_all_holdings:.2f}%]: {holdings}");
        }

        public List<decimal> OptimizePortfolio(List<Symbol> selectedSymbols)
        {
            var shortLookback = this._short_lookback;

            var history = History(selectedSymbols, shortLookback, Resolution.Daily)
                .SelectMany(slice => slice.Bars)
                .GroupBy(bar => bar.Key)
                .ToDictionary(group => group.Key, group => group.Select(g => g.Value.Close).ToList()); // Store the close prices for each symbol

            var returnsDict = selectedSymbols.ToDictionary(
                symbol => symbol,
                symbol => history[symbol]
                    .Select((price, index) => index == 0 ? 0 : (price - history[symbol][index - 1]) / history[symbol][index - 1]) // Calculate daily returns
                    .Skip(1) // shift 1 because the first day cannot be used for computing returns
                    .ToList()
            );

            int nAssets = selectedSymbols.Count;
            int nPortfolios = PNPortfolios;

            var results = new double[3, nPortfolios];
            var weightsRecord = new List<List<decimal>>();

            var random = new Random(PRandSeed);

            // Loop over each simulated portfolio
            for (int i = 0; i < nPortfolios; i++)
            {
                var weights = new List<decimal>();
                decimal sumWeights = 0;
                for (int j = 0; j < nAssets; j++)
                {
                    decimal w = (decimal)random.NextDouble();
                    weights.Add(w);
                    sumWeights += w;
                }

                for (int j = 0; j < nAssets; j++)
                {
                    weights[j] /= sumWeights;
                }

                decimal portfolioReturn = 0;
                decimal portfolioStdDev = 0;
                decimal downsideStdDev = 0;

                foreach (var symbol in selectedSymbols)
                {
                    var symbolReturns = returnsDict[symbol];
                    var weight = weights[selectedSymbols.IndexOf(symbol)];

                    portfolioReturn += symbolReturns.Average() * weight * shortLookback;

                    var symbolCovariance = Covariance(symbolReturns);
                    portfolioStdDev += (decimal)Math.Sqrt((double)(weight * symbolCovariance * shortLookback));

                    downsideStdDev += (decimal)Math.Sqrt((double)symbolReturns
                        .Where(x => x < 0)
                        .Select(x => (decimal)Math.Pow((double)x, 2))
                        .Sum() * (double)weight);
                }

                decimal sortinoRatio = portfolioReturn / downsideStdDev;

                results[0, i] = (double)portfolioReturn;
                results[1, i] = (double)portfolioStdDev;
                results[2, i] = (double)sortinoRatio;

                weightsRecord.Add(weights);
            }

            double maxSortino = double.MinValue;
            int bestSortinoIndex = -1;
            for (int i = 0; i < nPortfolios; i++)
            {
                if (results[2, i] > maxSortino)
                {
                    maxSortino = results[2, i];
                    bestSortinoIndex = i;
                }
            }

            return weightsRecord[bestSortinoIndex];
        }

        private static decimal Covariance(List<decimal> returns)
        {
            var avg = returns.Average();
            return returns.Select(r => (r - avg) * (r - avg)).Sum() / (returns.Count - 1);
        }


    }

}
