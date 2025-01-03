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
    /// <summary>
    /// A simple, textbook Protective Put model. 
    /// - For each invested equity, purchase near-OTM puts with 30–60 days to expiration.
    /// - 1 put contract per 100 shares (round down).
    /// - Minimal overhead: no trailing stops, no intraday logic, no rolling near expiry.
    /// </summary>
    public class ProtectivePutModel : RiskManagementModel
    {
        // ~3% OTM. For example, if equity Price=100, PutStrike=~97
        private readonly decimal _putStrikePercent = 0.97m;

        // We'll look for options with 30 to 60 days to expiration
        private readonly int _minDaysToExpiration = 30;
        private readonly int _maxDaysToExpiration = 60;

        // Liquidity constraints
        private readonly decimal _minOptionVolume = 50;
        private readonly decimal _maxBidAskSpread = 0.15m;

        // Keep track of which Option contracts we currently hold for each equity
        private Dictionary<Symbol, Symbol> _protectivePuts;
        // Make sure we add the Option chain only once per equity
        private HashSet<Symbol> _optionUniverses;

        public ProtectivePutModel()
        {
            _protectivePuts = new Dictionary<Symbol, Symbol>();
            _optionUniverses = new HashSet<Symbol>();
        }

        /// <summary>
        /// Called by the framework to manage portfolio risk.
        /// </summary>
        public override IEnumerable<IPortfolioTarget> ManageRisk(QCAlgorithm algorithm, IPortfolioTarget[] targets)
        {
            var riskAdjustedTargets = new List<IPortfolioTarget>();

            // For each invested equity, buy the corresponding protective puts
            foreach (var holdingKvp in algorithm.Portfolio)
            {
                var security = holdingKvp.Value;
                var symbol = security.Symbol;
                if (!security.Invested || symbol.SecurityType != SecurityType.Equity)
                    continue;

                // 1) Ensure we have an option chain for this equity
                if (!_optionUniverses.Contains(symbol))
                {
                    algorithm.AddOption(symbol);
                    _optionUniverses.Add(symbol);
                    // We'll wait until the next ManageRisk call after chain data is available
                    continue;
                }

                // 2) Retrieve the option chain
                var chain = algorithm.OptionChainProvider.GetOptionContractList(symbol, algorithm.Time);
                if (chain == null || !chain.Any()) 
                    continue;

                // 3) If we already hold a put, check if it's still invested
                if (_protectivePuts.TryGetValue(symbol, out var existingPutSymbol))
                {
                    if (!algorithm.Portfolio.ContainsKey(existingPutSymbol) ||
                        !algorithm.Portfolio[existingPutSymbol].Invested)
                    {
                        // We no longer hold that put => remove from dictionary
                        _protectivePuts.Remove(symbol);
                    }
                }

                // 4) If we don't currently hold a put, let's find one to buy
                if (!_protectivePuts.ContainsKey(symbol))
                {
                    var price = security.Price;
                    var targetStrike = price * _putStrikePercent;
                    var earliest = algorithm.Time.AddDays(_minDaysToExpiration);
                    var latest = algorithm.Time.AddDays(_maxDaysToExpiration);

                    // Filter for near-OTM puts with 30–60 DTE
                    var puts = chain
                        .Where(c =>
                            c.ID.OptionRight == OptionRight.Put &&
                            c.ID.Date >= earliest &&
                            c.ID.Date <= latest &&
                            c.ID.StrikePrice >= targetStrike * 0.98m &&
                            c.ID.StrikePrice <= targetStrike * 1.02m &&
                            algorithm.Securities.ContainsKey(c))
                        .OrderBy(c => Math.Abs((c.ID.Date - earliest).TotalDays))
                        .ThenBy(c => Math.Abs(c.ID.StrikePrice - targetStrike))
                        .ToList();

                    if (!puts.Any()) 
                        continue;

                    // Check liquidity
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

                    // Select the first "best match"
                    var selectedPut = liquidPuts.First();
                    _protectivePuts[symbol] = selectedPut;

                    // 1 put contract per each 100 shares
                    var equityShares = Math.Abs(security.Quantity);
                    var contractsNeeded = equityShares / 100;
                    if (contractsNeeded > 0)
                    {
                        riskAdjustedTargets.Add(new PortfolioTarget(selectedPut, contractsNeeded));
                        algorithm.Debug(
                            $"{algorithm.Time:yyyy-MM-dd HH:mm:ss} - Buying protective put for {symbol.Value} => {selectedPut} (qty={contractsNeeded})"
                        );
                    }
                }
                else
                {
                    // Possibly we'd hold the existing put until it expires or is no longer needed
                    // This simple approach doesn't proactively roll or close near-expiry puts
                }
            }

            // 5) If an equity is no longer invested, remove its put
            foreach (var kvp in _protectivePuts)
            {
                var equitySymbol = kvp.Key;
                if (!algorithm.Portfolio.ContainsKey(equitySymbol) ||
                    !algorithm.Portfolio[equitySymbol].Invested)
                {
                    var putSymbol = kvp.Value;
                    if (algorithm.Portfolio.ContainsKey(putSymbol) &&
                        algorithm.Portfolio[putSymbol].Invested)
                    {
                        // Liquidate the leftover put
                        riskAdjustedTargets.Add(new PortfolioTarget(putSymbol, 0));
                    }
                }
            }

            return riskAdjustedTargets;
        }
    }
}
