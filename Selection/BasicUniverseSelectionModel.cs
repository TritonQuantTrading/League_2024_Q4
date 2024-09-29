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
using Accord.MachineLearning;
using QuantConnect.Algorithm.CSharp;
#endregion

namespace QuantConnect
{

    public class BasicUniverseSelectionModel : FundamentalUniverseSelectionModel
    {
        public const int PNumCoarse = 200;
        public const int PNumFine = 70;

        private int _numCoarse;
        private int _numFine;
        public BasicUniverseSelectionModel(int numCoarse = PNumCoarse, int numFine = PNumFine)
        {
            this._numCoarse = numCoarse;
            this._numFine = numFine;
        }
        public override IEnumerable<Symbol> Select(QCAlgorithmFramework algorithm, IEnumerable<Fundamental> fundamental)
        {
            var selected = fundamental;
            // coarse selection
            selected = selected
                .Where(f => f.HasFundamentalData && f.Price > 5)
                .OrderByDescending(f => f.DollarVolume)
                .Take(this._numCoarse);
            // fine selection
            selected = selected
                .OrderByDescending(f => f.MarketCap)
                .Take(this._numFine);

            return selected.Select(f => f.Symbol);
        }
    }
}