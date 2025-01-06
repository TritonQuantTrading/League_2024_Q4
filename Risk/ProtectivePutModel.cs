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
        private readonly decimal _putStrikePercent = 0.97m;
        private readonly int _minDaysToExpiration = 90;
        private readonly int _maxDaysToExpiration = 120;
        private readonly decimal _minOptionVolume = 50;
        private readonly decimal _maxBidAskSpread = 0.15m;

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

            foreach (var holdingKvp in algorithm.Portfolio)
            {
                var security = holdingKvp.Value;
                var symbol = security.Symbol;
                if (!security.Invested || symbol.SecurityType != SecurityType.Equity)
                    continue;

                if (!_optionUniverses.Contains(symbol))
                {
                    algorithm.AddOption(symbol);
                    _optionUniverses.Add(symbol);
                    continue;
                }

                if (_protectivePuts.TryGetValue(symbol, out var existingPutSymbol))
                {
                    if (!algorithm.Portfolio.ContainsKey(existingPutSymbol) ||
                        !algorithm.Portfolio[existingPutSymbol].Invested)
                    {
                        _protectivePuts.Remove(symbol);
                    }
                }

                if (!_protectivePuts.ContainsKey(symbol))
                {
                    var price = security.Price;
                    var targetStrike = price * _putStrikePercent;
                    var earliest = algorithm.Time.AddDays(_minDaysToExpiration);
                    var latest = algorithm.Time.AddDays(_maxDaysToExpiration);

                    var chain = algorithm.OptionChainProvider.GetOptionContractList(symbol, algorithm.Time);
                    if (chain == null || !chain.Any()) 
                        continue;

                    var puts = chain
                        .Where(c =>
                            c.ID.OptionRight == OptionRight.Put &&
                            c.ID.Date >= earliest &&
                            c.ID.Date <= latest &&
                            c.ID.StrikePrice >= targetStrike * 0.98m &&
                            c.ID.StrikePrice <= targetStrike * 1.02m &&
                            algorithm.Securities.ContainsKey(c))
                        .OrderBy(c => Math.Abs((c.ID.Date - earliest).Days))
                        .ThenBy(c => Math.Abs(c.ID.StrikePrice - targetStrike))
                        .ToList();

                    if (!puts.Any()) 
                        continue;

                    var liquidPuts = puts.Where(contract =>
                    {
                        var opt = algorithm.Securities[contract];
                        var bid = opt.BidPrice;
                        var ask = opt.AskPrice;
                        var volume = opt.Volume;

                        if (ask <= 0) return false;
                        if (volume < _minOptionVolume) return false;

                        var spread = (ask - bid) / ask;
                        return spread <= _maxBidAskSpread;
                    }).ToList();

                    if (!liquidPuts.Any()) 
                        continue;

                    var selectedPut = liquidPuts.First();
                    _protectivePuts[symbol] = selectedPut;

                    var equityShares = Math.Abs(security.Quantity);
                    var contractsNeeded = Math.Floor(equityShares / 100);
                    if (contractsNeeded > 0)
                    {
                        riskAdjustedTargets.Add(new PortfolioTarget(selectedPut, contractsNeeded));
                    }
                }
            }

            foreach (var kvp in _protectivePuts.ToList())
            {
                var equitySymbol = kvp.Key;
                if (!algorithm.Portfolio.ContainsKey(equitySymbol) ||
                    !algorithm.Portfolio[equitySymbol].Invested)
                {
                    var putSymbol = kvp.Value;
                    if (algorithm.Portfolio.ContainsKey(putSymbol) &&
                        algorithm.Portfolio[putSymbol].Invested)
                    {
                        riskAdjustedTargets.Add(new PortfolioTarget(putSymbol, 0));
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