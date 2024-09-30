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

namespace QuantConnect {
    class MomentumAlphaModel: AlphaModel
    {
        // public constants
        public const int PLookback = 252;
        public const int PNumLong = 5;
        public const int PNumShort = 0;
        public override string Name {get; set;}
        private readonly int _lookback;
        private readonly int _numLong;
        private readonly int _numShort;
        private readonly Resolution _resolution;

        protected Dictionary<Symbol, SymbolData> _symbolData {get; set;}

        public MomentumAlphaModel(int lookback = PLookback, int numLong = PNumLong, int numShort = PNumShort, Resolution resolution = Resolution.Daily)
        {

            _lookback = lookback;
            _numLong = numLong;
            _numShort = numShort;
            _resolution = resolution;

            _symbolData = new Dictionary<Symbol, SymbolData>();
            Name = $"{nameof(MomentumAlphaModel)}({lookback},{numLong},{numShort},{resolution})";
        }

        public override IEnumerable<Insight> Update(QCAlgorithmFramework algorithm, Slice data)
        {
            var insights = new List<Insight>();
            return insights;
        }

        public override void OnSecuritiesChanged(QCAlgorithmFramework algorithm, SecurityChanges changes)
        {
        }

        public class SymbolData {
            // TODO: Add fields to store data for each symbol
        }

    }
}