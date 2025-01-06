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
    using QuantConnect.Commands;
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
    using QuantConnect.Securities.IndexOption;
    using QuantConnect.Securities.Interfaces;
    using QuantConnect.Securities.Volatility;
    using QuantConnect.Storage;
    using QuantConnect.Statistics;
    using QCAlgorithmFramework = QuantConnect.Algorithm.QCAlgorithm;
    using QCAlgorithmFrameworkBridge = QuantConnect.Algorithm.QCAlgorithm;
    using Calendar = QuantConnect.Data.Consolidators.Calendar;
#endregion
using System;
using System.Collections.Generic;
using System.Linq;
using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Algorithm.Framework.Portfolio;
using QuantConnect.Algorithm.Framework.Risk;
using QuantConnect.Securities;
using QuantConnect.Securities.Option;

namespace QuantConnect
{
    public class ProtectivePutModel : RiskManagementModel
    {
        private readonly decimal _baseStrikePercent = 0.97m;
        private readonly int _minDaysToExpiration = 90;
        private readonly int _maxDaysToExpiration = 120;

        private readonly decimal _minOptionVolume = 10;
        private readonly decimal _maxBidAskSpread = 0.30m;

        private Dictionary<Symbol, Symbol> _protectivePuts;
        private HashSet<Symbol> _optionUniverses;
        private DateTime _lastRebalance;

        public ProtectivePutModel()
        {
            _protectivePuts = new Dictionary<Symbol, Symbol>();
            _optionUniverses = new HashSet<Symbol>();
            _lastRebalance = DateTime.MinValue;
        }

        public override IEnumerable<IPortfolioTarget> ManageRisk(QCAlgorithm algorithm, IPortfolioTarget[] targets)
        {
            if (!ShouldRebalance(algorithm.Time))
                return Enumerable.Empty<IPortfolioTarget>();

            _lastRebalance = algorithm.Time;
            var riskAdjustedTargets = new List<IPortfolioTarget>();

            foreach (var kvp in algorithm.Portfolio)
            {
                var security = kvp.Value;
                var symbol = security.Symbol;

                if (!security.Invested || symbol.SecurityType != SecurityType.Equity)
                    continue;

                if (!_optionUniverses.Contains(symbol))
                {
                    algorithm.AddOption(symbol);
                    _optionUniverses.Add(symbol);
                    algorithm.Log($"[PUT_UNIVERSE] {algorithm.Time:yyyy-MM-dd} Adding options universe for {symbol.Value}");
                    continue;
                }

                if (_protectivePuts.TryGetValue(symbol, out var existingPutSymbol))
                {
                    if (algorithm.Portfolio.ContainsKey(existingPutSymbol)
                        && algorithm.Portfolio[existingPutSymbol].Invested)
                    {
                        algorithm.Log($"[PUT_DEBUG] {algorithm.Time:yyyy-MM-dd} {symbol.Value} has existing put {existingPutSymbol.Value} still invested. Skipping new put.");
                        continue;
                    }
                    else
                    {
                        _protectivePuts.Remove(symbol);
                        algorithm.Log($"[PUT_DEBUG] {algorithm.Time:yyyy-MM-dd} {symbol.Value} had a put {existingPutSymbol.Value} but not invested anymore. Will look for a new put.");
                    }
                }

                var chain = algorithm.OptionChainProvider.GetOptionContractList(symbol, algorithm.Time);
                if (chain == null)
                {
                    algorithm.Log($"[PUT_ERROR] {algorithm.Time:yyyy-MM-dd} {symbol.Value}: chain is null");
                    continue;
                }

                var totalContracts = chain.Count();
                if (totalContracts == 0)
                {
                    algorithm.Log($"[PUT_ERROR] {algorithm.Time:yyyy-MM-dd} {symbol.Value}: No option chain available (Count=0)");
                    continue;
                }
                algorithm.Log($"[PUT_DEBUG] {algorithm.Time:yyyy-MM-dd} {symbol.Value} chain.Count()={totalContracts}");

                var historyData = algorithm.History(symbol, 60, Resolution.Daily).ToList();
                if (historyData.Count < 60)
                {
                    algorithm.Log($"[PUT_ERROR] {algorithm.Time:yyyy-MM-dd} {symbol.Value}: Insufficient price history");
                    continue;
                }
                var startPrice = historyData.First().Close;
                var currentPrice = historyData.Last().Close;
                var priceReturn = (currentPrice - startPrice) / startPrice;

                decimal targetPrice;
                if (priceReturn >= 0.20m)
                {
                    targetPrice = currentPrice * 0.96m * (1m + priceReturn);
                    algorithm.Log($"[PUT_TARGET] {algorithm.Time:yyyy-MM-dd} {symbol.Value}: High return ({priceReturn:P2}), target protection at ${targetPrice:F2}");
                }
                else if (priceReturn >= 0.10m)
                {
                    targetPrice = currentPrice * 0.97m * (1m + priceReturn);
                    algorithm.Log($"[PUT_TARGET] {algorithm.Time:yyyy-MM-dd} {symbol.Value}: Medium return ({priceReturn:P2}), target protection at ${targetPrice:F2}");
                }
                else
                {
                    targetPrice = currentPrice * _baseStrikePercent;
                    algorithm.Log($"[PUT_TARGET] {algorithm.Time:yyyy-MM-dd} {symbol.Value}: Base case, target protection at ${targetPrice:F2}");
                }

                var allExpiries = chain
                    .Select(c => c.ID.Date)
                    .Distinct()
                    .OrderBy(d => d)
                    .ToList();
                algorithm.Log($"[PUT_EXPIRY] {algorithm.Time:yyyy-MM-dd} {symbol.Value}: All expiries: {string.Join(", ", allExpiries.Select(d => d.ToString("yyyy-MM-dd")))}");

                var desiredExpiries = allExpiries
                    .Where(d => (d - algorithm.Time).TotalDays >= _minDaysToExpiration
                             && (d - algorithm.Time).TotalDays <= _maxDaysToExpiration)
                    .OrderBy(d => d)
                    .ToList();
                algorithm.Log($"[PUT_EXPIRY_DESIRED] {algorithm.Time:yyyy-MM-dd} {symbol.Value}: Desired expiries ({_minDaysToExpiration}-{_maxDaysToExpiration} days): {string.Join(", ", desiredExpiries.Select(d => d.ToString("yyyy-MM-dd")))}");

                if (!desiredExpiries.Any())
                {
                    algorithm.Log($"[PUT_ERROR] {algorithm.Time:yyyy-MM-dd} {symbol.Value}: No puts found at desired expiry");
                    continue;
                }

                var validPuts = new List<Symbol>();
                
                foreach (var expiry in desiredExpiries)
                {
                    var putsAtExpiry = chain
                        .Where(c => c.ID.OptionRight == OptionRight.Put && c.ID.Date == expiry)
                        .OrderByDescending(c => c.ID.StrikePrice)
                        .ToList();

                    if (!putsAtExpiry.Any())
                    {
                        algorithm.Log($"[PUT_DEBUG] {algorithm.Time:yyyy-MM-dd} {symbol.Value}: expiry={expiry:yyyy-MM-dd} => no Put contracts");
                        continue;
                    }

                    algorithm.Log($"[PUT_DEBUG] {algorithm.Time:yyyy-MM-dd} {symbol.Value}: expiry={expiry:yyyy-MM-dd}, found {putsAtExpiry.Count} put(s). Now checking each...");

                    foreach (var put in putsAtExpiry)
                    {
                        if (!algorithm.Securities.ContainsKey(put))
                        {
                            algorithm.AddOptionContract(put, Resolution.Minute);
                            algorithm.Log($"[PUT_DEBUG] skip put={put.Value}, reason=Securities not exist. Possibly data not added yet for this contract?");
                        }

                        if (!algorithm.Securities.ContainsKey(put))
                        {
                            algorithm.Log($"[PUT_DEBUG] {put.Value} still no data this bar. Might skip or wait next bar.");
                            continue;
                        }

                        var opt = algorithm.Securities[put];
                        var volume = opt.Volume;
                        var bid = opt.BidPrice;
                        var ask = opt.AskPrice;

                        algorithm.Log($"[PUT_DETAIL] {algorithm.Time:yyyy-MM-dd} {symbol.Value}: Strike={put.ID.StrikePrice:F2}, Bid={bid:F2}, Ask={ask:F2}, Vol={volume}");

                        if (ask <= 0 || bid <= 0)
                        {
                            algorithm.Log($"[PUT_REJECT] ask={ask}, bid={bid}, skip invalid quotes");
                            continue;
                        }

                        var spread = (ask - bid) / ask;
                        if (volume < _minOptionVolume)
                        {
                            algorithm.Log($"[PUT_REJECT] Volume {volume} < {_minOptionVolume}");
                            continue;
                        }
                        if (spread > _maxBidAskSpread)
                        {
                            algorithm.Log($"[PUT_REJECT] Spread {spread:P2} > {_maxBidAskSpread:P2}");
                            continue;
                        }

                        validPuts.Add(put);
                    }
                }

                if (!validPuts.Any())
                {
                    algorithm.Log($"[PUT_ERROR] {algorithm.Time:yyyy-MM-dd} {symbol.Value}: No valid puts found after liquidity filtering");
                    continue;
                }

                var selectedPut = validPuts
                    .OrderBy(p => Math.Abs(p.ID.StrikePrice - targetPrice))
                    .First();

                var equityShares = Math.Abs(security.Quantity);
                var contractsNeeded = Math.Floor(equityShares / 100);

                if (selectedPut.ID.StrikePrice < targetPrice && targetPrice > 0)
                {
                    var coverageRatio = selectedPut.ID.StrikePrice / targetPrice;
                    if (coverageRatio > 0)
                        contractsNeeded = Math.Floor(contractsNeeded / coverageRatio);
                }

                if (contractsNeeded > 0)
                {
                    _protectivePuts[symbol] = selectedPut;
                    riskAdjustedTargets.Add(new PortfolioTarget(selectedPut, contractsNeeded));
                    algorithm.Log($"[PUT_TRADE] {algorithm.Time:yyyy-MM-dd} {symbol.Value}: Put={selectedPut.Value}, Strike={selectedPut.ID.StrikePrice:F2}, Contracts={contractsNeeded}");
                }
                else
                {
                    algorithm.Log($"[PUT_DEBUG] {algorithm.Time:yyyy-MM-dd} {symbol.Value}: contractsNeeded=0, skip placing put orders");
                }
            }

            foreach (var kvpProtective in _protectivePuts.ToList())
            {
                var equitySymbol = kvpProtective.Key;
                var putSymbol = kvpProtective.Value;

                if (!algorithm.Portfolio.ContainsKey(equitySymbol) || !algorithm.Portfolio[equitySymbol].Invested)
                {
                    if (algorithm.Portfolio.ContainsKey(putSymbol) && algorithm.Portfolio[putSymbol].Invested)
                    {
                        riskAdjustedTargets.Add(new PortfolioTarget(putSymbol, 0));
                        algorithm.Log($"[PUT_LIQUIDATE] {algorithm.Time:yyyy-MM-dd} Liquidate put={putSymbol.Value}, equity={equitySymbol.Value}");
                    }
                    _protectivePuts.Remove(equitySymbol);
                }
            }

            return riskAdjustedTargets;
        }

        private bool ShouldRebalance(DateTime currentTime)
        {
            if (_lastRebalance == DateTime.MinValue)
                return true;
            var timeSinceLastRebalance = currentTime - _lastRebalance;
            return timeSinceLastRebalance.Days >= 30;
        }
    }
}
