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

    class TempAlphaModel : AlphaModel
    {
        public override string Name { get; set; }
        private readonly Resolution _resolution;
        // private InsightCollection _insightCollection = new();

        /// <summary>
        /// Dictionary containing basic information for each symbol present as key
        /// </summary>
        // protected Dictionary<Symbol, SymbolData> _symbolData { get; init; }

        public TempAlphaModel(
            Resolution resolution = Resolution.Daily
        )
        {
            // Define a unique name for the alpha model but 
            // should be consistent across different backtests
            // (i.e. don't use Guid.NewGuid().ToString() here)
            // You can use the a comb of the private fields given by the constructor params
            _resolution = resolution;
            // Name = $"{nameof(MacdAlphaModel)}({fastPeriod},{slowPeriod},{signalPeriod},{movingAverageType},{resolution})";
            Name = $"{nameof(TempAlphaModel)}({resolution})";
        }
        public override IEnumerable<Insight> Update(QCAlgorithm algorithm, Slice data)
        {
            // TODO: Check out the impl of the example alpha models in the repo:
            // https://github.com/QuantConnect/Lean/tree/master/Algorithm.Framework/Alphas
            return new List<Insight>();
        }

        // private List<Security> _securities = new List<Security>();

        public override void OnSecuritiesChanged(QCAlgorithm algorithm, SecurityChanges changes)
        {
            // You can also call the base.OnSecuritiesChanged(algorithm, changes) to handle the insights changes.
            // foreach (var security in changes.AddedSecurities)
            // {
            //     var dynamicSecurity = security as dynamic;
            //     dynamicSecurity.Sma = algorithm.SMA(security.Symbol, 20);
            //     dynamicSecurity.Rsi = algorithm.RSI(security.Symbol, 14, MovingAverageType.Simple, Resolution.Daily);
            //     algorithm.WarmUpIndicator(security.Symbol, dynamicSecurity.Sma);
            //     _securities.Add(security);
            // }

            // foreach (var security in changes.RemovedSecurities)
            // {
            //     if (_securities.Contains(security))
            //     {
            //         algorithm.DeregisterIndicator((security as dynamic).Sma);
            //         algorithm.DeregisterIndicator((security as dynamic).Rsi);
            //         _securities.Remove(security);
            //     }
            // }
        }
        public class SymbolData {
            // TODO: Necessary fields for the symbol data
        }
    }
}