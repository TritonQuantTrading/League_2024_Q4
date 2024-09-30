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
    /// <summary>
    /// This universe selection model will chain to the security changes of a given <see cref="UniverseSelectionModel"/> selection
    /// output and create a new <see cref="OptionChainUniverse"/> for each of them
    /// 
    /// This class is based on https://www.quantconnect.com/docs/v2/writing-algorithms/algorithm-framework/universe-selection/options-universes#61-Option-Chained-Universe-Selection
    /// instead of attaching to a Universe, it attaches to a UniverseSelectionModel
    /// </summary>
    public class DerivedOptionsUniverseSelectionModel : UniverseSelectionModel
    {
        private DateTime _nextRefreshTimeUtc;
        private readonly UniverseSelectionModel _universeSelectionModel;
        private readonly Func<OptionFilterUniverse, OptionFilterUniverse> _optionFilter;
        private readonly Dictionary<Universe, IEnumerable<Symbol>> _fundamentalUniverses;

        /// <summary>
        /// Gets the next time the framework should invoke the `CreateUniverses` method to refresh the set of universes.
        /// </summary>
        public override DateTime GetNextRefreshTimeUtc()
        {
            var parentRefreshTime = _universeSelectionModel.GetNextRefreshTimeUtc();
            if (parentRefreshTime <= _nextRefreshTimeUtc)
            {
                _fundamentalUniverses.Clear();
                _nextRefreshTimeUtc = parentRefreshTime;
            }
            return _nextRefreshTimeUtc;
        }

        /// <summary>
        /// Creates a new instance of <see cref="DerivedOptionsUniverseSelectionModel"/>
        /// </summary>
        /// <param name="universeSelectionModel">The universe selection model we want to chain to</param>
        /// <param name="optionFilter">The option filter universe to use</param>
        public DerivedOptionsUniverseSelectionModel(UniverseSelectionModel universeSelectionModel, Func<OptionFilterUniverse, OptionFilterUniverse> optionFilter = null)
        {
            _nextRefreshTimeUtc = DateTime.MaxValue;
            _universeSelectionModel = universeSelectionModel;
            _optionFilter = optionFilter;
            _fundamentalUniverses = new Dictionary<Universe, IEnumerable<Symbol>>();
        }

        /// <summary>
        /// Creates the universes for this algorithm. Called when the original universeSelectionModel
        /// or when the symbols it contains change
        /// </summary>
        /// <param name="algorithm">The algorithm instance to create universes for</param>
        /// <returns>The universes to be used by the algorithm</returns>
        public override IEnumerable<Universe> CreateUniverses(QCAlgorithm algorithm)
        {
            _nextRefreshTimeUtc = DateTime.MaxValue;

            if (_fundamentalUniverses.Count <= 0)
            {
                var universes = _universeSelectionModel.CreateUniverses(algorithm);

                foreach (var universe in universes)
                {
                    _fundamentalUniverses.Add(universe, Enumerable.Empty<Symbol>());
                    universe.SelectionChanged += (sender, args) =>
                    {
                        // We must create the new option Symbol using the CreateOption(Symbol, ...) overload.
                        // Otherwise, we'll end up loading equity data for the selected Symbol, which won't
                        // work whenever we're loading options data for any non-equity underlying asset class.
                        _fundamentalUniverses[universe] = ((Universe.SelectionEventArgs)args).CurrentSelection
                            .Select(symbol => Symbol.CreateOption(
                                symbol,
                                symbol.ID.Market,
                                symbol.SecurityType.DefaultOptionStyle(),
                                default(OptionRight),
                                0m,
                                SecurityIdentifier.DefaultDate))
                            .ToList();

                        _nextRefreshTimeUtc = DateTime.MinValue;
                    };
                }
            }

            foreach (var kpv in _fundamentalUniverses)
            {
                yield return kpv.Key;

                foreach (var optionSymbol in kpv.Value)
                {
                    yield return algorithm.CreateOptionChain(optionSymbol, _optionFilter, kpv.Key.UniverseSettings);
                }
            }
        }
    }
}