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

    public class MomentumUniverseSelectionModel : FundamentalUniverseSelectionModel
    {

        private int _lookback;
        private int _numCoarse;
        private int _numFine;
        private int _numLong;
        private decimal _adjustmentStep;
        private int _shortLookback;
        private League2024Q4 _algo;
        public MomentumUniverseSelectionModel(League2024Q4 algo, int lookback, int numCoarse, int numFine, int numLong, decimal adjustmentStep, int shortLookback)
        {
            this._algo = algo;
            this._lookback = lookback;
            this._numCoarse = numCoarse;
            this._numFine = numFine;
            this._numLong = numLong;
            this._adjustmentStep = adjustmentStep;
            this._shortLookback = shortLookback;
        }
        public override IEnumerable<Symbol> Select(QCAlgorithmFramework algorithm, IEnumerable<Fundamental> fundamental)
        {
            // TODO: the part related to this._algo should be removed in the future to avoid functionality overlap
            /************************************************************/
            /** Start of the part that should be removed in the future **/
            if (this._algo.nextAdjustmentDate != null && this._algo.Time < this._algo.nextAdjustmentDate)
            {
                return this._algo.Universe.Unchanged;
            }
            this._algo._rebalance = true;
            if (this._algo.firstTradeDate == null)
            {
                this._algo.firstTradeDate = this._algo.Time;
                this._algo.nextAdjustmentDate = League2024Q4.GetNextAdjustmentDate(this._algo.Time);
                this._algo._rebalance = true;
            }
            /**  End of the part that should be removed in the future  **/
            /************************************************************/
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